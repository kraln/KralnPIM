using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;
using GraphEvent = Microsoft.Graph.Models.Event;

namespace PIM.Sync.Graph;

public sealed class GraphCalendarProvider : ICalendarProvider
{
    private const string ResourceType = "calendar";

    private readonly GraphAuthProvider _authProvider;
    private readonly ISyncStateRepository _syncStateRepo;
    private readonly ILogger<GraphCalendarProvider> _logger;
    private readonly IReadOnlySet<string>? _allowedCalendarIds;
    private GraphServiceClient? _client;
    private List<string> _calendarIds = [];

    public string AccountId { get; }

    public GraphCalendarProvider(
        string accountId,
        GraphAuthProvider authProvider,
        ISyncStateRepository syncStateRepo,
        ILogger<GraphCalendarProvider> logger,
        IReadOnlySet<string>? allowedCalendarIds = null)
    {
        AccountId = accountId;
        _authProvider = authProvider;
        _syncStateRepo = syncStateRepo;
        _logger = logger;
        _allowedCalendarIds = allowedCalendarIds;
    }

    public async Task AuthenticateAsync(CancellationToken ct)
    {
        var token = await _authProvider.GetAccessTokenAsync(ct);
        _client = GraphClientFactory.Create(token);

        if (_allowedCalendarIds is null or { Count: 0 })
        {
            _calendarIds = [];
            _logger.LogInformation(
                "Graph Calendar authenticated for {AccountId}, no calendars configured — skipping sync",
                AccountId);
            return;
        }

        // Discover calendars, then filter to configured set
        var calendars = await _client.Me.Calendars.GetAsync(cancellationToken: ct);
        _calendarIds = calendars?.Value?
            .Where(c => !string.IsNullOrEmpty(c.Id) && _allowedCalendarIds.Contains(c.Id))
            .Select(c => c.Id!)
            .ToList() ?? [];

        _logger.LogInformation("Graph Calendar authenticated for {AccountId}, syncing {Count} calendars",
            AccountId, _calendarIds.Count);
    }

    public async Task<SyncResult<CalendarEvent>> SyncEventsAsync(
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        EnsureClient();

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
        EnsureClient();

        var graphEvent = GraphCalendarMapper.ToGraphEvent(evt);
        await _client!.Me.Calendars[evt.CalendarId].Events
            .PostAsync(graphEvent, cancellationToken: ct);

        _logger.LogInformation("Created event '{Summary}' on calendar {CalendarId}",
            evt.Summary, evt.CalendarId);
    }

    public async Task UpdateEventAsync(CalendarEvent evt, CancellationToken ct)
    {
        EnsureClient();

        var graphEvent = GraphCalendarMapper.ToGraphEvent(evt);
        await _client!.Me.Events[evt.EventId]
            .PatchAsync(graphEvent, cancellationToken: ct);

        _logger.LogInformation("Updated event '{Summary}' ({EventId})", evt.Summary, evt.EventId);
    }

    public async Task DeleteEventAsync(string eventId, CancellationToken ct)
    {
        EnsureClient();

        try
        {
            await _client!.Me.Events[eventId].DeleteAsync(cancellationToken: ct);
            _logger.LogInformation("Deleted event {EventId}", eventId);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            _logger.LogWarning("Event {EventId} not found", eventId);
        }
    }

    private async Task<SyncResult<CalendarEvent>> FullSyncCalendarAsync(
        string calendarId, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        var allEvents = new List<GraphEvent>();
        string? deltaLink = null;

        var response = await _client!.Me.Calendars[calendarId].CalendarView.Delta
            .GetAsDeltaGetResponseAsync(config =>
            {
                config.QueryParameters.StartDateTime = rangeStart.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
                config.QueryParameters.EndDateTime = rangeEnd.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            }, ct);

        while (response is not null)
        {
            if (response.Value is not null)
                allEvents.AddRange(response.Value);

            if (response.OdataNextLink is not null)
            {
                response = await _client.Me.Calendars[calendarId].CalendarView.Delta
                    .WithUrl(response.OdataNextLink)
                    .GetAsDeltaGetResponseAsync(cancellationToken: ct);
            }
            else
            {
                deltaLink = response.OdataDeltaLink;
                break;
            }
        }

        var upserted = MapEventsWithSeriesMasters(allEvents, calendarId);

        if (deltaLink is not null)
        {
            await _syncStateRepo.SetAsync(AccountId, $"{ResourceType}:{calendarId}",
                DateTimeOffset.UtcNow, deltaLink, ct);
        }

        return new SyncResult<CalendarEvent>(upserted, [], deltaLink);
    }

    private async Task<SyncResult<CalendarEvent>> DeltaSyncCalendarAsync(
        string calendarId, string deltaLink,
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        try
        {
            return await DeltaSyncCalendarCoreAsync(calendarId, deltaLink, ct);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 410)
        {
            _logger.LogWarning("Delta token expired for calendar {CalendarId}, performing full sync",
                calendarId);
            return await FullSyncCalendarAsync(calendarId, rangeStart, rangeEnd, ct);
        }
    }

    private async Task<SyncResult<CalendarEvent>> DeltaSyncCalendarCoreAsync(
        string calendarId, string deltaLink, CancellationToken ct)
    {
        var allEvents = new List<GraphEvent>();
        var deletedIds = new List<string>();
        string? newDeltaLink = null;

        var response = await _client!.Me.Calendars[calendarId].CalendarView.Delta
            .WithUrl(deltaLink)
            .GetAsDeltaGetResponseAsync(cancellationToken: ct);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                foreach (var evt in response.Value)
                {
                    if (evt.AdditionalData?.ContainsKey("@removed") == true ||
                        evt.IsCancelled == true)
                    {
                        if (evt.Id is not null)
                            deletedIds.Add(evt.Id);
                    }
                    else
                    {
                        allEvents.Add(evt);
                    }
                }
            }

            if (response.OdataNextLink is not null)
            {
                response = await _client.Me.Calendars[calendarId].CalendarView.Delta
                    .WithUrl(response.OdataNextLink)
                    .GetAsDeltaGetResponseAsync(cancellationToken: ct);
            }
            else
            {
                newDeltaLink = response.OdataDeltaLink;
                break;
            }
        }

        var upserted = MapEventsWithSeriesMasters(allEvents, calendarId);

        if (newDeltaLink is not null)
        {
            await _syncStateRepo.SetAsync(AccountId, $"{ResourceType}:{calendarId}",
                DateTimeOffset.UtcNow, newDeltaLink, ct);
        }

        return new SyncResult<CalendarEvent>(upserted, deletedIds, newDeltaLink);
    }

    /// <summary>
    /// The delta endpoint returns series masters (with subject/isAllDay but historical dates)
    /// and occurrences (with correct dates but missing subject/isAllDay). This method builds
    /// a lookup of series masters, inherits their properties onto occurrences, and skips the
    /// series masters themselves from the output.
    /// </summary>
    private List<CalendarEvent> MapEventsWithSeriesMasters(List<GraphEvent> allEvents, string calendarId)
    {
        var masters = new Dictionary<string, GraphEvent>();
        foreach (var evt in allEvents)
        {
            if (evt.Type?.ToString() == "SeriesMaster" && evt.Id is not null)
                masters[evt.Id] = evt;
        }

        var results = new List<CalendarEvent>();
        foreach (var evt in allEvents)
        {
            // Skip series masters — we only want the occurrences with real dates
            if (evt.Type?.ToString() == "SeriesMaster")
                continue;

            // Inherit missing fields from series master
            if (evt.SeriesMasterId is not null &&
                masters.TryGetValue(evt.SeriesMasterId, out var master))
            {
                if (string.IsNullOrEmpty(evt.Subject))
                    evt.Subject = master.Subject;
                evt.IsAllDay ??= master.IsAllDay;
                if (evt.Location is null || string.IsNullOrEmpty(evt.Location.DisplayName))
                    evt.Location = master.Location;
            }

            results.Add(GraphCalendarMapper.ToCalendarEvent(evt, AccountId, calendarId));
        }

        return results;
    }

    private void EnsureClient()
    {
        if (_client is null)
            throw new InvalidOperationException("Call AuthenticateAsync before using the provider.");
    }
}
