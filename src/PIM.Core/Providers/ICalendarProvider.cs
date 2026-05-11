using PIM.Core.Models;

namespace PIM.Core.Providers;

public interface ICalendarProvider
{
    string AccountId { get; }
    Task AuthenticateAsync(CancellationToken ct);
    Task<SyncResult<CalendarEvent>> SyncEventsAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct);
    Task<string> CreateEventAsync(CalendarEvent evt, CancellationToken ct);
    Task UpdateEventAsync(CalendarEvent evt, CancellationToken ct);
    Task DeleteEventAsync(string eventId, CancellationToken ct);
}
