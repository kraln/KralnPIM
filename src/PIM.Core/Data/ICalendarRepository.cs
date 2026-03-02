using PIM.Core.Models;

namespace PIM.Core.Data;

public interface ICalendarRepository
{
    Task UpsertEventsAsync(IEnumerable<CalendarEvent> events, CancellationToken ct = default);
    Task<List<CalendarEvent>> GetEventsInRangeAsync(DateTimeOffset start, DateTimeOffset end, string? accountId = null, CancellationToken ct = default);
    Task DeleteEventAsync(string eventId, CancellationToken ct = default);
    Task PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
