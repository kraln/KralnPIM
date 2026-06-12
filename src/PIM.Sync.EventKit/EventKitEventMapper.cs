using System.Globalization;
using PIM.Core.Models;
using PimEventStatus = PIM.Core.Models.EventStatus;

namespace PIM.Sync.EventKit;

internal static class EventKitEventMapper
{
    /// Compose the SQLite primary key. Recurring/detached occurrences share
    /// the master eventIdentifier, so the occurrenceDate is appended to
    /// disambiguate. One-off events keep their plain id.
    internal static string ComposeEventId(string id, string? occurrenceDate)
        => occurrenceDate is null ? id : $"{id}@{occurrenceDate}";

    public static CalendarEvent ToCalendarEvent(EventKitEventDto dto, string accountId)
    {
        var start = DateTimeOffset.Parse(dto.Start, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var end = DateTimeOffset.Parse(dto.End, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        var invitees = dto.Attendees
            .Select(a => a.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!)
            .ToList();

        return new CalendarEvent(
            EventId: ComposeEventId(dto.Id, dto.OccurrenceDate),
            AccountId: accountId,
            CalendarId: dto.CalendarId,
            Summary: dto.Title,
            Description: dto.Notes,
            Start: start,
            End: end,
            IsAllDay: dto.IsAllDay,
            Location: dto.Location,
            Invitees: invitees,
            RecurrenceRule: null,
            Status: MapStatus(dto.Status),
            Transparency: string.Equals(dto.Availability, "free", StringComparison.OrdinalIgnoreCase)
                ? Transparency.Free
                : Transparency.Busy
        );
    }

    private static PimEventStatus MapStatus(string status) => status switch
    {
        "tentative" => PimEventStatus.Tentative,
        "canceled" => PimEventStatus.Cancelled,
        _ => PimEventStatus.Confirmed,
    };
}
