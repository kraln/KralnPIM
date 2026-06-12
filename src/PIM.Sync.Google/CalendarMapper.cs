using PIM.Core.Models;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;
using GoogleEventDateTime = Google.Apis.Calendar.v3.Data.EventDateTime;
using GoogleEventAttendee = Google.Apis.Calendar.v3.Data.EventAttendee;

namespace PIM.Sync.Google;

public static class CalendarMapper
{
    public static CalendarEvent ToCalendarEvent(GoogleEvent evt, string accountId, string calendarId)
    {
        var (start, isAllDay) = ParseStart(evt);
        var end = ParseEnd(evt, isAllDay);
        var status = MapStatus(evt.Status);
        var invitees = evt.Attendees?
            .Where(a => !string.IsNullOrEmpty(a.Email))
            .Select(a => a.Email!)
            .ToList() ?? [];
        var recurrenceRule = evt.Recurrence?.FirstOrDefault(r =>
            r.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));

        return new CalendarEvent(
            EventId: evt.Id,
            AccountId: accountId,
            CalendarId: calendarId,
            Summary: evt.Summary ?? "",
            Description: evt.Description,
            Start: start,
            End: end,
            IsAllDay: isAllDay,
            Location: evt.Location,
            Invitees: invitees,
            RecurrenceRule: recurrenceRule,
            Status: status,
            Transparency: string.Equals(evt.Transparency, "transparent", StringComparison.OrdinalIgnoreCase)
                ? Transparency.Free
                : Transparency.Busy
        );
    }

    public static GoogleEvent ToGoogleEvent(CalendarEvent evt)
    {
        var googleEvent = new GoogleEvent
        {
            Id = evt.EventId,
            Summary = evt.Summary,
            Description = evt.Description,
            Location = evt.Location,
            Status = MapStatusToGoogle(evt.Status),
        };

        if (evt.IsAllDay)
        {
            googleEvent.Start = new GoogleEventDateTime
            {
                Date = evt.Start.ToString("yyyy-MM-dd"),
            };
            googleEvent.End = new GoogleEventDateTime
            {
                Date = evt.End.ToString("yyyy-MM-dd"),
            };
        }
        else
        {
            googleEvent.Start = new GoogleEventDateTime
            {
                DateTimeDateTimeOffset = evt.Start,
            };
            googleEvent.End = new GoogleEventDateTime
            {
                DateTimeDateTimeOffset = evt.End,
            };
        }

        if (evt.Invitees.Count > 0)
        {
            googleEvent.Attendees = evt.Invitees
                .Select(email => new GoogleEventAttendee { Email = email })
                .ToList();
        }

        if (evt.RecurrenceRule is not null)
        {
            googleEvent.Recurrence = [evt.RecurrenceRule];
        }

        return googleEvent;
    }

    private static (DateTimeOffset Start, bool IsAllDay) ParseStart(GoogleEvent evt)
    {
        if (evt.Start?.DateTimeDateTimeOffset is not null)
            return (evt.Start.DateTimeDateTimeOffset.Value, false);

        if (!string.IsNullOrEmpty(evt.Start?.Date) &&
            DateOnly.TryParse(evt.Start.Date, out var dateOnly))
            return (AllDayMidnight(dateOnly), true);

        return (DateTimeOffset.MinValue, false);
    }

    private static DateTimeOffset ParseEnd(GoogleEvent evt, bool isAllDay)
    {
        if (!isAllDay && evt.End?.DateTimeDateTimeOffset is not null)
            return evt.End.DateTimeDateTimeOffset.Value;

        if (isAllDay && !string.IsNullOrEmpty(evt.End?.Date) &&
            DateOnly.TryParse(evt.End.Date, out var dateOnly))
            return AllDayMidnight(dateOnly);

        return DateTimeOffset.MinValue;
    }

    private static DateTimeOffset AllDayMidnight(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    private static EventStatus MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "tentative" => EventStatus.Tentative,
        "cancelled" => EventStatus.Cancelled,
        _ => EventStatus.Confirmed,
    };

    private static string MapStatusToGoogle(EventStatus status) => status switch
    {
        EventStatus.Tentative => "tentative",
        EventStatus.Cancelled => "cancelled",
        _ => "confirmed",
    };
}
