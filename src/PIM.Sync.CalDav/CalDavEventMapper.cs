using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using PIM.Core.Models;
using ICalEvent = Ical.Net.CalendarComponents.CalendarEvent;
using PimEvent = PIM.Core.Models.CalendarEvent;
using PimEventStatus = PIM.Core.Models.EventStatus;

namespace PIM.Sync.CalDav;

public static class CalDavEventMapper
{
    public static PimEvent ToCalendarEvent(ICalEvent icalEvent, string accountId, string calendarId)
    {
        var (start, end, isAllDay) = ParseDateTimes(icalEvent);
        var status = MapStatus(icalEvent.Status);
        var invitees = icalEvent.Attendees?
            .Where(a => a.Value is not null)
            .Select(a => ExtractEmail(a.Value))
            .Where(email => !string.IsNullOrEmpty(email))
            .ToList() ?? [];
        var recurrenceRule = icalEvent.RecurrenceRules?.FirstOrDefault()?.ToString();
        if (recurrenceRule is not null && !recurrenceRule.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
        {
            recurrenceRule = "RRULE:" + recurrenceRule;
        }

        return new PimEvent(
            EventId: icalEvent.Uid ?? "",
            AccountId: accountId,
            CalendarId: calendarId,
            Summary: icalEvent.Summary ?? "",
            Description: icalEvent.Description,
            Start: start,
            End: end,
            IsAllDay: isAllDay,
            Location: icalEvent.Location,
            Invitees: invitees,
            RecurrenceRule: recurrenceRule,
            Status: status
        );
    }

    public static string ToICalendar(PimEvent evt)
    {
        var calendar = new Calendar();
        calendar.Properties.Add(new CalendarProperty("PRODID", "-//KralnPIM//CalDAV//EN"));

        var icalEvent = new ICalEvent
        {
            Uid = evt.EventId,
            Summary = evt.Summary,
            Description = evt.Description,
            Location = evt.Location,
            Status = MapStatusToICal(evt.Status),
        };

        if (evt.IsAllDay)
        {
            icalEvent.DtStart = new CalDateTime(evt.Start.Year, evt.Start.Month, evt.Start.Day);
            icalEvent.DtStart.HasTime = false;
            icalEvent.DtEnd = new CalDateTime(evt.End.Year, evt.End.Month, evt.End.Day);
            icalEvent.DtEnd.HasTime = false;
        }
        else
        {
            icalEvent.DtStart = new CalDateTime(evt.Start.UtcDateTime, "UTC");
            icalEvent.DtEnd = new CalDateTime(evt.End.UtcDateTime, "UTC");
        }

        foreach (var email in evt.Invitees)
        {
            icalEvent.Attendees.Add(new Attendee($"mailto:{email}"));
        }

        if (evt.RecurrenceRule is not null)
        {
            var ruleText = evt.RecurrenceRule;
            if (ruleText.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
                ruleText = ruleText[6..];
            icalEvent.RecurrenceRules.Add(new RecurrencePattern(ruleText));
        }

        calendar.Events.Add(icalEvent);

        var serializer = new CalendarSerializer();
        return serializer.SerializeToString(calendar);
    }

    private static (DateTimeOffset Start, DateTimeOffset End, bool IsAllDay) ParseDateTimes(ICalEvent evt)
    {
        if (evt.DtStart is null)
            return (DateTimeOffset.MinValue, DateTimeOffset.MinValue, false);

        bool isAllDay = !evt.DtStart.HasTime;

        DateTimeOffset start;
        DateTimeOffset end;

        if (isAllDay)
        {
            var d = evt.DtStart.Date;
            var dt = new DateTime(d.Year, d.Month, d.Day);
            start = new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
            if (evt.DtEnd is not null)
            {
                var de = evt.DtEnd.Date;
                var dte = new DateTime(de.Year, de.Month, de.Day);
                end = new DateTimeOffset(dte, TimeZoneInfo.Local.GetUtcOffset(dte));
            }
            else
            {
                end = start.AddDays(1);
            }
        }
        else
        {
            start = new DateTimeOffset(evt.DtStart.AsUtc, TimeSpan.Zero);
            end = evt.DtEnd is not null
                ? new DateTimeOffset(evt.DtEnd.AsUtc, TimeSpan.Zero)
                : start;
        }

        return (start, end, isAllDay);
    }

    private static string ExtractEmail(Uri uri)
    {
        // mailto:alice@example.com → alice@example.com
        var schemePrefix = "mailto:";
        var abs = uri.AbsoluteUri;
        return abs.StartsWith(schemePrefix, StringComparison.OrdinalIgnoreCase)
            ? abs[schemePrefix.Length..]
            : abs;
    }

    private static PimEventStatus MapStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "TENTATIVE" => PimEventStatus.Tentative,
        "CANCELLED" => PimEventStatus.Cancelled,
        _ => PimEventStatus.Confirmed,
    };

    private static string MapStatusToICal(PimEventStatus status) => status switch
    {
        PimEventStatus.Tentative => "TENTATIVE",
        PimEventStatus.Cancelled => "CANCELLED",
        _ => "CONFIRMED",
    };
}
