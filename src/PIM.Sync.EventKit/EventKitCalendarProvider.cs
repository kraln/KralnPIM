using Microsoft.Extensions.Logging;
using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.Sync.EventKit;

public sealed class EventKitCalendarProvider : ICalendarProvider
{
    private readonly EventKitClient _client;
    private readonly HashSet<string>? _allowedCalendarIds;
    private readonly ILogger<EventKitCalendarProvider> _logger;

    public string AccountId { get; }

    internal EventKitCalendarProvider(
        string accountId,
        EventKitClient client,
        HashSet<string>? allowedCalendarIds,
        ILogger<EventKitCalendarProvider> logger)
    {
        AccountId = accountId;
        _client = client;
        _allowedCalendarIds = allowedCalendarIds;
        _logger = logger;
    }

    public EventKitCalendarProvider(
        string accountId,
        string binaryPath,
        HashSet<string>? allowedCalendarIds,
        ILoggerFactory loggerFactory)
        : this(
            accountId,
            new EventKitClient(binaryPath, loggerFactory.CreateLogger<EventKitClient>()),
            allowedCalendarIds,
            loggerFactory.CreateLogger<EventKitCalendarProvider>())
    {
    }

    /// EventKit auth lives in the macOS TCC layer, attached to the helper
    /// binary identity. The first call to the helper triggers the system
    /// prompt and grants the permission for the lifetime of the codesigned
    /// binary. Probing list-calendars here acts as both a liveness check
    /// on the binary and a forcing function for the TCC dialog at sync-init
    /// rather than mid-sync.
    public async Task AuthenticateAsync(CancellationToken ct)
    {
        var calendars = await _client.ListCalendarsAsync(ct);
        _logger.LogInformation(
            "EventKit authenticated for {AccountId}: {Count} calendars visible",
            AccountId, calendars.Count);
    }

    public async Task<SyncResult<CalendarEvent>> SyncEventsAsync(
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        if (_allowedCalendarIds is null || _allowedCalendarIds.Count == 0)
        {
            _logger.LogDebug("EventKit sync skipped for {AccountId}: no allowed calendars", AccountId);
            return new SyncResult<CalendarEvent>([], [], null);
        }

        var dtos = await _client.FetchEventsAsync(rangeStart, rangeEnd, _allowedCalendarIds, ct);
        var events = dtos.Select(d => EventKitEventMapper.ToCalendarEvent(d, AccountId)).ToList();

        _logger.LogInformation(
            "EventKit sync for {AccountId}: {Count} events across {Calendars} calendars",
            AccountId, events.Count, _allowedCalendarIds.Count);

        // Full-window re-hash on every tick: eventkit-cli has no ctag/etag
        // equivalent and a one-shot subprocess can't subscribe to
        // EKEventStoreChanged notifications. The upstream repository's
        // INSERT OR REPLACE handles the upsert; deletes within the window
        // are not detected here yet (would require diffing against the
        // prior tick's id set).
        return new SyncResult<CalendarEvent>(events, [], null);
    }

    public Task<string> CreateEventAsync(CalendarEvent evt, CancellationToken ct) =>
        throw new NotSupportedException("EventKit provider is read-only.");

    public Task UpdateEventAsync(CalendarEvent evt, CancellationToken ct) =>
        throw new NotSupportedException("EventKit provider is read-only.");

    public Task DeleteEventAsync(string eventId, CancellationToken ct) =>
        throw new NotSupportedException("EventKit provider is read-only.");
}
