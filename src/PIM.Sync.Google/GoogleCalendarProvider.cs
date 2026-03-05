using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;

namespace PIM.Sync.Google;

public sealed class GoogleCalendarProvider : ICalendarProvider
{
    private const string ResourceType = "calendar";

    private readonly GoogleCredentialManager _credentialManager;
    private readonly ISyncStateRepository _syncStateRepo;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly ILogger<GoogleCalendarProvider> _logger;
    private readonly IReadOnlySet<string>? _allowedCalendarIds;
    private CalendarService? _service;
    private List<string> _calendarIds = [];

    public string AccountId { get; }

    public GoogleCalendarProvider(
        string accountId,
        GoogleCredentialManager credentialManager,
        ISyncStateRepository syncStateRepo,
        TokenBucketRateLimiter rateLimiter,
        ILogger<GoogleCalendarProvider> logger,
        IReadOnlySet<string>? allowedCalendarIds = null)
    {
        AccountId = accountId;
        _credentialManager = credentialManager;
        _syncStateRepo = syncStateRepo;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _allowedCalendarIds = allowedCalendarIds;
    }

    public async Task AuthenticateAsync(CancellationToken ct)
    {
        var credential = await _credentialManager.EnsureAuthenticatedAsync(ct);
        _service = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "KralnPIM",
        });

        if (_allowedCalendarIds is null or { Count: 0 })
        {
            _calendarIds = [];
            _logger.LogInformation(
                "Google Calendar authenticated for {AccountId}, no calendars configured — skipping sync",
                AccountId);
            return;
        }

        // Discover calendars, then filter to configured set
        await _rateLimiter.WaitAsync(1, ct);
        var calendarList = await _service.CalendarList.List().ExecuteAsync(ct);
        _calendarIds = calendarList.Items?
            .Select(c => c.Id)
            .Where(id => !string.IsNullOrEmpty(id) && _allowedCalendarIds.Contains(id))
            .ToList() ?? [];

        _logger.LogInformation("Google Calendar authenticated for {AccountId}, syncing {Count} calendars",
            AccountId, _calendarIds.Count);
    }

    public async Task<SyncResult<CalendarEvent>> SyncEventsAsync(
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        EnsureService();

        var allUpserted = new List<CalendarEvent>();
        var allDeletedIds = new List<string>();
        string? newSyncToken = null;

        foreach (var calendarId in _calendarIds)
        {
            var (_, syncToken) = await _syncStateRepo.GetAsync(
                AccountId, $"{ResourceType}:{calendarId}", ct);

            if (syncToken is not null)
            {
                var delta = await DeltaSyncCalendarAsync(calendarId, syncToken, rangeStart, rangeEnd, ct);
                allUpserted.AddRange(delta.Upserted);
                allDeletedIds.AddRange(delta.DeletedIds);
                newSyncToken = delta.NewSyncToken ?? newSyncToken;
            }
            else
            {
                var full = await FullSyncCalendarAsync(calendarId, rangeStart, rangeEnd, ct);
                allUpserted.AddRange(full.Upserted);
                newSyncToken = full.NewSyncToken ?? newSyncToken;
            }
        }

        return new SyncResult<CalendarEvent>(allUpserted, allDeletedIds, newSyncToken);
    }

    public async Task CreateEventAsync(CalendarEvent evt, CancellationToken ct)
    {
        EnsureService();
        await _rateLimiter.WaitAsync(10, ct);

        var googleEvent = CalendarMapper.ToGoogleEvent(evt);
        await _service!.Events.Insert(googleEvent, evt.CalendarId).ExecuteAsync(ct);

        _logger.LogInformation("Created event '{Summary}' on calendar {CalendarId}",
            evt.Summary, evt.CalendarId);
    }

    public async Task UpdateEventAsync(CalendarEvent evt, CancellationToken ct)
    {
        EnsureService();
        await _rateLimiter.WaitAsync(10, ct);

        var googleEvent = CalendarMapper.ToGoogleEvent(evt);
        await _service!.Events.Update(googleEvent, evt.CalendarId, evt.EventId).ExecuteAsync(ct);

        _logger.LogInformation("Updated event '{Summary}' ({EventId})", evt.Summary, evt.EventId);
    }

    public async Task DeleteEventAsync(string eventId, CancellationToken ct)
    {
        EnsureService();
        await _rateLimiter.WaitAsync(10, ct);

        // Try to delete from all known calendars — only one will succeed
        foreach (var calendarId in _calendarIds)
        {
            try
            {
                await _service!.Events.Delete(calendarId, eventId).ExecuteAsync(ct);
                _logger.LogInformation("Deleted event {EventId} from calendar {CalendarId}",
                    eventId, calendarId);
                return;
            }
            catch (global::Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Not on this calendar, try next
            }
        }

        _logger.LogWarning("Event {EventId} not found on any calendar", eventId);
    }

    private async Task<SyncResult<CalendarEvent>> FullSyncCalendarAsync(
        string calendarId, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        var upserted = new List<CalendarEvent>();
        string? pageToken = null;
        string? syncToken = null;

        do
        {
            await _rateLimiter.WaitAsync(5, ct);
            var request = _service!.Events.List(calendarId);
            request.TimeMinDateTimeOffset = rangeStart;
            request.TimeMaxDateTimeOffset = rangeEnd;
            request.SingleEvents = true;
            request.MaxResults = 250;
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);

            if (response.Items is not null)
            {
                foreach (var evt in response.Items)
                    upserted.Add(CalendarMapper.ToCalendarEvent(evt, AccountId, calendarId));
            }

            syncToken = response.NextSyncToken ?? syncToken;
            pageToken = response.NextPageToken;
        } while (pageToken is not null);

        if (syncToken is not null)
        {
            await _syncStateRepo.SetAsync(AccountId, $"{ResourceType}:{calendarId}",
                DateTimeOffset.UtcNow, syncToken, ct);
        }

        return new SyncResult<CalendarEvent>(upserted, [], syncToken);
    }

    private async Task<SyncResult<CalendarEvent>> DeltaSyncCalendarAsync(
        string calendarId, string syncToken,
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        try
        {
            return await DeltaSyncCalendarCoreAsync(calendarId, syncToken, ct);
        }
        catch (global::Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
        {
            // Sync token expired — fall back to full sync
            _logger.LogWarning("Sync token expired for calendar {CalendarId}, performing full sync", calendarId);
            return await FullSyncCalendarAsync(calendarId, rangeStart, rangeEnd, ct);
        }
    }

    private async Task<SyncResult<CalendarEvent>> DeltaSyncCalendarCoreAsync(
        string calendarId, string syncToken, CancellationToken ct)
    {
        var upserted = new List<CalendarEvent>();
        var deletedIds = new List<string>();
        string? pageToken = null;
        string? newSyncToken = null;

        do
        {
            await _rateLimiter.WaitAsync(5, ct);
            var request = _service!.Events.List(calendarId);
            request.SyncToken = syncToken;
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);

            if (response.Items is not null)
            {
                foreach (var evt in response.Items)
                {
                    if (evt.Status == "cancelled")
                        deletedIds.Add(evt.Id);
                    else
                        upserted.Add(CalendarMapper.ToCalendarEvent(evt, AccountId, calendarId));
                }
            }

            newSyncToken = response.NextSyncToken ?? newSyncToken;
            pageToken = response.NextPageToken;
        } while (pageToken is not null);

        if (newSyncToken is not null)
        {
            await _syncStateRepo.SetAsync(AccountId, $"{ResourceType}:{calendarId}",
                DateTimeOffset.UtcNow, newSyncToken, ct);
        }

        return new SyncResult<CalendarEvent>(upserted, deletedIds, newSyncToken);
    }

    private void EnsureService()
    {
        if (_service is null)
            throw new InvalidOperationException("Call AuthenticateAsync before using the provider.");
    }
}
