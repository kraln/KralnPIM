using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;
using PIM.Server.Models;
using PIM.Server.Registration;

namespace PIM.Server.Services;

public sealed class FreeBusySinkService
{
    private static readonly TimeSpan BridgeGap = TimeSpan.FromMinutes(5);

    private readonly ProviderRegistry _registry;
    private readonly ICalendarRepository _calendarRepo;
    private readonly ISyncStateRepository _syncStateRepo;
    private readonly StorageConfig _storageConfig;
    private readonly UiConfig _uiConfig;
    private readonly ILogger<FreeBusySinkService> _logger;

    public FreeBusySinkService(
        ProviderRegistry registry,
        ICalendarRepository calendarRepo,
        ISyncStateRepository syncStateRepo,
        PimConfig config,
        ILogger<FreeBusySinkService> logger)
    {
        _registry = registry;
        _calendarRepo = calendarRepo;
        _syncStateRepo = syncStateRepo;
        _storageConfig = config.Storage;
        _uiConfig = config.Ui;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        var sinks = _registry.Sinks;
        if (sinks.Count == 0)
            return;

        var rangeStart = DateTimeOffset.UtcNow.AddMonths(-_storageConfig.BufferMonthsBack);
        var rangeEnd = DateTimeOffset.UtcNow.AddMonths(_storageConfig.BufferMonthsForward);

        var allEvents = await _calendarRepo.GetEventsInRangeAsync(rangeStart, rangeEnd, accountId: null, ct);

        var sinkKeys = sinks.Select(s => (s.AccountId, s.CalendarId)).ToHashSet();
        var sourceEvents = allEvents
            .Where(e => e.Status != EventStatus.Cancelled)
            .Where(e => e.Transparency != Transparency.Free)
            .Where(e => !sinkKeys.Contains((e.AccountId, e.CalendarId)))
            .ToList();

        var localTz = ResolveTimezone(_uiConfig.TimezonePrimary);
        var blocks = Coalesce(sourceEvents, localTz);
        var hash = HashBlocks(blocks);

        foreach (var sink in sinks)
        {
            try
            {
                await RefreshSinkAsync(sink, blocks, hash, ct);
            }
            catch (ReauthorizationRequiredException)
            {
                _logger.LogWarning("Skipping free/busy sink {AccountId}/{CalendarId}: re-authorization required",
                    sink.AccountId, sink.CalendarId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to refresh free/busy sink {AccountId}/{CalendarId}",
                    sink.AccountId, sink.CalendarId);
            }
        }
    }

    private async Task RefreshSinkAsync(SinkInfo sink, List<BusyBlock> blocks, string desiredHash, CancellationToken ct)
    {
        var resourceType = $"freebusy-sink:{sink.CalendarId}";
        var (_, shadowJson) = await _syncStateRepo.GetAsync(sink.AccountId, resourceType, ct);

        FreeBusyShadow? shadow = null;
        if (!string.IsNullOrEmpty(shadowJson))
        {
            try { shadow = JsonSerializer.Deserialize(shadowJson, ServerJsonContext.Default.FreeBusyShadow); }
            catch (JsonException ex) { _logger.LogWarning(ex, "Corrupt free/busy shadow for {AccountId}/{CalendarId}, treating as empty", sink.AccountId, sink.CalendarId); }
        }

        if (shadow is not null && shadow.Hash == desiredHash)
        {
            _logger.LogDebug("Free/busy sink {AccountId}/{CalendarId} unchanged ({Blocks} blocks), skipping",
                sink.AccountId, sink.CalendarId, blocks.Count);
            return;
        }

        if (shadow is not null)
        {
            foreach (var oldId in shadow.EventIds)
            {
                try { await sink.Provider.DeleteEventAsync(oldId, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException and not ReauthorizationRequiredException)
                {
                    _logger.LogWarning(ex, "Failed to delete stale free/busy event {EventId} on {AccountId}/{CalendarId}",
                        oldId, sink.AccountId, sink.CalendarId);
                }
            }
        }

        var newIds = new List<string>(blocks.Count);
        foreach (var block in blocks)
        {
            var evt = new CalendarEvent(
                EventId: Guid.NewGuid().ToString("N"),
                AccountId: sink.AccountId,
                CalendarId: sink.CalendarId,
                Summary: "Busy",
                Description: null,
                Start: block.Start,
                End: block.End,
                IsAllDay: false,
                Location: null,
                Invitees: [],
                RecurrenceRule: null,
                Status: EventStatus.Confirmed);

            var assignedId = await sink.Provider.CreateEventAsync(evt, ct);
            newIds.Add(assignedId);
        }

        var newShadow = new FreeBusyShadow(desiredHash, newIds);
        var newJson = JsonSerializer.Serialize(newShadow, ServerJsonContext.Default.FreeBusyShadow);
        await _syncStateRepo.SetAsync(sink.AccountId, resourceType, DateTimeOffset.UtcNow, newJson, ct);

        _logger.LogInformation("Refreshed free/busy sink {AccountId}/{CalendarId}: {Blocks} busy blocks",
            sink.AccountId, sink.CalendarId, blocks.Count);
    }

    internal static List<BusyBlock> Coalesce(IEnumerable<CalendarEvent> events, TimeZoneInfo localTz)
    {
        var intervals = events
            .Select(e => ToInterval(e, localTz))
            .Where(i => i.End > i.Start)
            .OrderBy(i => i.Start)
            .ToList();

        var merged = new List<BusyBlock>();
        foreach (var (s, e) in intervals)
        {
            if (merged.Count == 0 || merged[^1].End + BridgeGap < s)
            {
                merged.Add(new BusyBlock(s, e));
            }
            else if (e > merged[^1].End)
            {
                merged[^1] = merged[^1] with { End = e };
            }
        }
        return merged;
    }

    private static (DateTimeOffset Start, DateTimeOffset End) ToInterval(CalendarEvent evt, TimeZoneInfo localTz)
    {
        if (!evt.IsAllDay)
            return (evt.Start, evt.End);

        // All-day events: render in the user's primary timezone.
        var startLocal = TimeZoneInfo.ConvertTime(evt.Start, localTz);
        var endLocal = TimeZoneInfo.ConvertTime(evt.End, localTz);
        var startMidnight = new DateTimeOffset(startLocal.Year, startLocal.Month, startLocal.Day, 0, 0, 0, startLocal.Offset);
        var endMidnight = new DateTimeOffset(endLocal.Year, endLocal.Month, endLocal.Day, 0, 0, 0, endLocal.Offset);
        if (endMidnight <= startMidnight)
            endMidnight = startMidnight.AddDays(1);
        return (startMidnight, endMidnight);
    }

    internal static string HashBlocks(IReadOnlyList<BusyBlock> blocks)
    {
        var sb = new StringBuilder();
        foreach (var b in blocks)
        {
            sb.Append(b.Start.UtcTicks);
            sb.Append('-');
            sb.Append(b.End.UtcTicks);
            sb.Append('|');
        }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes, 0, 16);
    }

    private TimeZoneInfo ResolveTimezone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger.LogWarning("Unknown timezone '{Tz}', falling back to UTC", id);
            return TimeZoneInfo.Utc;
        }
    }

    public sealed record BusyBlock(DateTimeOffset Start, DateTimeOffset End);
}
