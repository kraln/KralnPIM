using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Server.Models;
using PIM.Server.Registration;
using PIM.Server.Services;
using PIM.Server.WebSocket;

namespace PIM.Server.Api;

internal static class CalendarEndpoints
{
    internal static void MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calendar/events");

        group.MapGet("/", async (
            ICalendarRepository repo,
            DateTimeOffset? start,
            DateTimeOffset? end,
            string? accountId,
            CancellationToken ct) =>
        {
            var rangeStart = start ?? DateTimeOffset.UtcNow.Date;
            var rangeEnd = end ?? rangeStart.AddDays(7);
            var events = await repo.GetEventsInRangeAsync(rangeStart, rangeEnd, accountId, ct);
            return Results.Ok(events);
        });

        group.MapPost("/", async (
            CalendarEvent evt,
            ICalendarRepository repo,
            ProviderRegistry registry,
            AccountStatusTracker tracker,
            WebSocketBroadcaster broadcaster,
            CancellationToken ct) =>
        {
            if (!tracker.IsOnline(evt.AccountId))
                return Results.Json(new ErrorResponse($"Account '{evt.AccountId}' is offline."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 503);

            var providers = registry.GetCalendarProviders(evt.AccountId);
            if (providers.Count == 0)
                return Results.Json(new ErrorResponse($"No calendar provider for account '{evt.AccountId}'."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 404);

            await providers[0].CreateEventAsync(evt, ct);
            await repo.UpsertEventsAsync([evt], ct);
            await broadcaster.BroadcastAsync(new CalendarSyncEvent(evt.AccountId, 1), ct);
            return Results.Created($"/api/calendar/events/{evt.EventId}", evt);
        });

        group.MapPut("/{eventId}", async (
            string eventId,
            CalendarEvent evt,
            ICalendarRepository repo,
            ProviderRegistry registry,
            AccountStatusTracker tracker,
            WebSocketBroadcaster broadcaster,
            CancellationToken ct) =>
        {
            if (!tracker.IsOnline(evt.AccountId))
                return Results.Json(new ErrorResponse($"Account '{evt.AccountId}' is offline."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 503);

            var providers = registry.GetCalendarProviders(evt.AccountId);
            if (providers.Count == 0)
                return Results.Json(new ErrorResponse($"No calendar provider for account '{evt.AccountId}'."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 404);

            await providers[0].UpdateEventAsync(evt, ct);
            await repo.UpsertEventsAsync([evt], ct);
            await broadcaster.BroadcastAsync(new CalendarSyncEvent(evt.AccountId, 1), ct);
            return Results.Ok(evt);
        });

        group.MapDelete("/{eventId}", async (
            string eventId,
            ICalendarRepository repo,
            ProviderRegistry registry,
            AccountStatusTracker tracker,
            WebSocketBroadcaster broadcaster,
            CancellationToken ct) =>
        {
            // Look up the event to find its account
            var now = DateTimeOffset.UtcNow;
            var events = await repo.GetEventsInRangeAsync(now.AddYears(-2), now.AddYears(2), ct: ct);
            var evt = events.FirstOrDefault(e => e.EventId == eventId);

            if (evt is null)
                return Results.Json(new ErrorResponse("Event not found."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 404);

            if (!tracker.IsOnline(evt.AccountId))
                return Results.Json(new ErrorResponse($"Account '{evt.AccountId}' is offline."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 503);

            var providers = registry.GetCalendarProviders(evt.AccountId);
            if (providers.Count > 0)
                await providers[0].DeleteEventAsync(eventId, ct);

            await repo.DeleteEventAsync(eventId, ct);
            await broadcaster.BroadcastAsync(new CalendarSyncEvent(evt.AccountId, 1), ct);
            return Results.NoContent();
        });
    }
}
