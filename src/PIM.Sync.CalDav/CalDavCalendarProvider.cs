using System.Text.Json;
using Ical.Net;
using Microsoft.Extensions.Logging;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.Sync.CalDav;

public sealed class CalDavCalendarProvider : ICalendarProvider
{
    private const string ResourceType = "calendar";
    private const string EtagResourcePrefix = "calendar-etags:";

    private readonly string _calendarId;
    private readonly string _calendarUrl;
    private readonly string _username;
    private readonly IAuthRepository _authRepo;
    private readonly ISyncStateRepository _syncStateRepo;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CalDavCalendarProvider> _logger;
    private CalDavClient? _client;

    public string AccountId { get; }

    public CalDavCalendarProvider(
        string accountId,
        string calendarId,
        string calendarUrl,
        string username,
        IAuthRepository authRepo,
        ISyncStateRepository syncStateRepo,
        HttpClient httpClient,
        ILogger<CalDavCalendarProvider> logger)
    {
        AccountId = accountId;
        _calendarId = calendarId;
        _calendarUrl = calendarUrl;
        _username = username;
        _authRepo = authRepo;
        _syncStateRepo = syncStateRepo;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task AuthenticateAsync(CancellationToken ct)
    {
        var password = await _authRepo.GetImapPasswordAsync(AccountId, ct)
            ?? throw new InvalidOperationException($"No password found for account {AccountId}");

        _client = new CalDavClient(_httpClient, _calendarUrl, _username, password, _logger);

        // Verify connectivity by fetching ctag
        var ctag = await _client.GetCtagAsync(ct);
        _logger.LogInformation("CalDAV authenticated for {AccountId}, ctag={Ctag}", AccountId, ctag);
    }

    public async Task<SyncResult<CalendarEvent>> SyncEventsAsync(
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        EnsureClient();

        var (_, storedCtag) = await _syncStateRepo.GetAsync(
            AccountId, $"{ResourceType}:{_calendarId}", ct);

        var currentCtag = await _client!.GetCtagAsync(ct);

        if (storedCtag is not null && storedCtag == currentCtag)
        {
            _logger.LogDebug("CalDAV ctag unchanged for {CalendarId}, skipping sync", _calendarId);
            return new SyncResult<CalendarEvent>([], [], currentCtag);
        }

        if (storedCtag is not null)
        {
            return await DeltaSyncAsync(currentCtag, rangeStart, rangeEnd, ct);
        }

        return await FullSyncAsync(currentCtag, rangeStart, rangeEnd, ct);
    }

    public async Task CreateEventAsync(CalendarEvent evt, CancellationToken ct)
    {
        EnsureClient();

        var icalData = CalDavEventMapper.ToICalendar(evt);
        var href = $"{evt.EventId}.ics";
        await _client!.PutEventAsync(href, icalData, ct);

        _logger.LogInformation("Created CalDAV event '{Summary}' ({EventId})", evt.Summary, evt.EventId);
    }

    public async Task UpdateEventAsync(CalendarEvent evt, CancellationToken ct)
    {
        EnsureClient();

        var icalData = CalDavEventMapper.ToICalendar(evt);
        var href = $"{evt.EventId}.ics";
        await _client!.PutEventAsync(href, icalData, ct);

        _logger.LogInformation("Updated CalDAV event '{Summary}' ({EventId})", evt.Summary, evt.EventId);
    }

    public async Task DeleteEventAsync(string eventId, CancellationToken ct)
    {
        EnsureClient();

        var href = $"{eventId}.ics";
        await _client!.DeleteEventAsync(href, ct);

        _logger.LogInformation("Deleted CalDAV event {EventId}", eventId);
    }

    private async Task<SyncResult<CalendarEvent>> FullSyncAsync(
        string? currentCtag, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        var eventData = await _client!.GetAllEventsAsync(rangeStart, rangeEnd, ct);
        var events = ParseEvents(eventData);

        var etags = await _client.GetEtagsAsync(ct);
        await SaveSyncStateAsync(currentCtag, etags, ct);

        _logger.LogInformation("CalDAV full sync for {CalendarId}: {Count} events", _calendarId, events.Count);
        return new SyncResult<CalendarEvent>(events, [], currentCtag);
    }

    private async Task<SyncResult<CalendarEvent>> DeltaSyncAsync(
        string? currentCtag, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        var storedEtags = await LoadStoredEtagsAsync(ct);
        var currentEtags = await _client!.GetEtagsAsync(ct);

        // Find changed or new hrefs
        var changedHrefs = currentEtags
            .Where(kv => !storedEtags.TryGetValue(kv.Key, out var oldEtag) || oldEtag != kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        // Find deleted hrefs
        var deletedHrefs = storedEtags.Keys
            .Where(href => !currentEtags.ContainsKey(href))
            .ToList();

        var upserted = new List<CalendarEvent>();
        if (changedHrefs.Count > 0)
        {
            var eventData = await _client.GetEventsAsync(changedHrefs, ct);
            upserted = ParseEvents(eventData);
        }

        var deletedIds = deletedHrefs
            .Select(ExtractEventIdFromHref)
            .ToList();

        await SaveSyncStateAsync(currentCtag, currentEtags, ct);

        _logger.LogInformation("CalDAV delta sync for {CalendarId}: {Upserted} upserted, {Deleted} deleted",
            _calendarId, upserted.Count, deletedIds.Count);
        return new SyncResult<CalendarEvent>(upserted, deletedIds, currentCtag);
    }

    private List<CalendarEvent> ParseEvents(Dictionary<string, string> hrefToIcalData)
    {
        var events = new List<CalendarEvent>();

        foreach (var (href, icalData) in hrefToIcalData)
        {
            try
            {
                var calendar = Calendar.Load(icalData);
                foreach (var icalEvent in calendar.Events)
                {
                    events.Add(CalDavEventMapper.ToCalendarEvent(icalEvent, AccountId, _calendarId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse iCalendar data from {Href}", href);
            }
        }

        return events;
    }

    private async Task SaveSyncStateAsync(string? ctag, Dictionary<string, string> etags, CancellationToken ct)
    {
        await _syncStateRepo.SetAsync(
            AccountId, $"{ResourceType}:{_calendarId}",
            DateTimeOffset.UtcNow, ctag, ct);

        var etagJson = JsonSerializer.Serialize(etags);
        await _syncStateRepo.SetAsync(
            AccountId, $"{EtagResourcePrefix}{_calendarId}",
            DateTimeOffset.UtcNow, etagJson, ct);
    }

    private async Task<Dictionary<string, string>> LoadStoredEtagsAsync(CancellationToken ct)
    {
        var (_, etagJson) = await _syncStateRepo.GetAsync(
            AccountId, $"{EtagResourcePrefix}{_calendarId}", ct);

        if (etagJson is null)
            return new Dictionary<string, string>();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(etagJson) ?? new Dictionary<string, string>();
    }

    private static string ExtractEventIdFromHref(string href)
    {
        var filename = href.Split('/').Last();
        return filename.EndsWith(".ics", StringComparison.OrdinalIgnoreCase)
            ? filename[..^4]
            : filename;
    }

    private void EnsureClient()
    {
        if (_client is null)
            throw new InvalidOperationException("Call AuthenticateAsync before using the provider.");
    }
}
