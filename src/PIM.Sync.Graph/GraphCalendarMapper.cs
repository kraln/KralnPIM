using PIM.Core.Models;
using GraphEvent = Microsoft.Graph.Models.Event;
using GraphDateTimeTimeZone = Microsoft.Graph.Models.DateTimeTimeZone;
using GraphAttendee = Microsoft.Graph.Models.Attendee;
using GraphEmailAddress = Microsoft.Graph.Models.EmailAddress;
using GraphPatternedRecurrence = Microsoft.Graph.Models.PatternedRecurrence;
using GraphRecurrencePattern = Microsoft.Graph.Models.RecurrencePattern;
using GraphRecurrenceRange = Microsoft.Graph.Models.RecurrenceRange;

namespace PIM.Sync.Graph;

public static class GraphCalendarMapper
{
    public static CalendarEvent ToCalendarEvent(GraphEvent evt, string accountId, string calendarId)
    {
        var (start, isAllDay) = ParseStart(evt);
        var end = ParseEnd(evt, isAllDay);
        var status = MapStatus(evt);

        var invitees = evt.Attendees?
            .Where(a => !string.IsNullOrEmpty(a.EmailAddress?.Address))
            .Select(a => a.EmailAddress!.Address!)
            .ToList() ?? [];

        var recurrenceRule = ConvertRecurrenceToRRule(evt.Recurrence);

        return new CalendarEvent(
            EventId: evt.Id ?? "",
            AccountId: accountId,
            CalendarId: calendarId,
            Summary: evt.Subject ?? "",
            Description: evt.Body?.Content,
            Start: start,
            End: end,
            IsAllDay: isAllDay,
            Location: evt.Location?.DisplayName,
            Invitees: invitees,
            RecurrenceRule: recurrenceRule,
            Status: status
        );
    }

    public static GraphEvent ToGraphEvent(CalendarEvent evt)
    {
        var graphEvent = new GraphEvent
        {
            Id = evt.EventId,
            Subject = evt.Summary,
            Body = evt.Description is not null
                ? new Microsoft.Graph.Models.ItemBody
                {
                    ContentType = Microsoft.Graph.Models.BodyType.Text,
                    Content = evt.Description,
                }
                : null,
            Location = evt.Location is not null
                ? new Microsoft.Graph.Models.Location { DisplayName = evt.Location }
                : null,
            IsAllDay = evt.IsAllDay,
        };

        if (evt.IsAllDay)
        {
            graphEvent.Start = new GraphDateTimeTimeZone
            {
                DateTime = evt.Start.UtcDateTime.ToString("yyyy-MM-dd'T'00:00:00.0000000"),
                TimeZone = "UTC",
            };
            graphEvent.End = new GraphDateTimeTimeZone
            {
                DateTime = evt.End.UtcDateTime.ToString("yyyy-MM-dd'T'00:00:00.0000000"),
                TimeZone = "UTC",
            };
        }
        else
        {
            graphEvent.Start = new GraphDateTimeTimeZone
            {
                DateTime = evt.Start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"),
                TimeZone = "UTC",
            };
            graphEvent.End = new GraphDateTimeTimeZone
            {
                DateTime = evt.End.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"),
                TimeZone = "UTC",
            };
        }

        if (evt.Invitees.Count > 0)
        {
            graphEvent.Attendees = evt.Invitees
                .Select(email => new GraphAttendee
                {
                    EmailAddress = new GraphEmailAddress { Address = email },
                })
                .ToList();
        }

        if (evt.RecurrenceRule is not null)
            graphEvent.Recurrence = ConvertRRuleToRecurrence(evt.RecurrenceRule);

        return graphEvent;
    }

    private static (DateTimeOffset Start, bool IsAllDay) ParseStart(GraphEvent evt)
    {
        if (evt.IsAllDay == true && evt.Start is not null)
        {
            if (DateTime.TryParse(evt.Start.DateTime, out var date))
                return (new DateTimeOffset(date, TimeSpan.Zero), true);
        }

        if (evt.Start is not null)
        {
            var tz = ParseTimeZone(evt.Start.TimeZone);
            if (DateTime.TryParse(evt.Start.DateTime, out var dt))
                return (new DateTimeOffset(dt, tz.GetUtcOffset(dt)), false);
        }

        return (DateTimeOffset.MinValue, false);
    }

    private static DateTimeOffset ParseEnd(GraphEvent evt, bool isAllDay)
    {
        if (evt.End is null)
            return DateTimeOffset.MinValue;

        if (isAllDay)
        {
            if (DateTime.TryParse(evt.End.DateTime, out var date))
                return new DateTimeOffset(date, TimeSpan.Zero);
        }
        else
        {
            var tz = ParseTimeZone(evt.End.TimeZone);
            if (DateTime.TryParse(evt.End.DateTime, out var dt))
                return new DateTimeOffset(dt, tz.GetUtcOffset(dt));
        }

        return DateTimeOffset.MinValue;
    }

    private static TimeZoneInfo ParseTimeZone(string? timeZone)
    {
        if (string.IsNullOrEmpty(timeZone) || timeZone == "UTC")
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static EventStatus MapStatus(GraphEvent evt)
    {
        if (evt.IsCancelled == true)
            return EventStatus.Cancelled;

        return evt.ResponseStatus?.Response switch
        {
            Microsoft.Graph.Models.ResponseType.TentativelyAccepted => EventStatus.Tentative,
            _ => EventStatus.Confirmed,
        };
    }

    internal static string? ConvertRecurrenceToRRule(GraphPatternedRecurrence? recurrence)
    {
        if (recurrence?.Pattern is null)
            return null;

        var pattern = recurrence.Pattern;
        var parts = new List<string>();

        var freq = pattern.Type switch
        {
            Microsoft.Graph.Models.RecurrencePatternType.Daily => "DAILY",
            Microsoft.Graph.Models.RecurrencePatternType.Weekly => "WEEKLY",
            Microsoft.Graph.Models.RecurrencePatternType.AbsoluteMonthly => "MONTHLY",
            Microsoft.Graph.Models.RecurrencePatternType.RelativeMonthly => "MONTHLY",
            Microsoft.Graph.Models.RecurrencePatternType.AbsoluteYearly => "YEARLY",
            Microsoft.Graph.Models.RecurrencePatternType.RelativeYearly => "YEARLY",
            _ => null,
        };

        if (freq is null)
            return null;

        parts.Add($"FREQ={freq}");

        if (pattern.Interval is > 1)
            parts.Add($"INTERVAL={pattern.Interval}");

        if (pattern.DaysOfWeek is { Count: > 0 })
        {
            var days = pattern.DaysOfWeek.Select(d => d switch
            {
                Microsoft.Graph.Models.DayOfWeekObject.Monday => "MO",
                Microsoft.Graph.Models.DayOfWeekObject.Tuesday => "TU",
                Microsoft.Graph.Models.DayOfWeekObject.Wednesday => "WE",
                Microsoft.Graph.Models.DayOfWeekObject.Thursday => "TH",
                Microsoft.Graph.Models.DayOfWeekObject.Friday => "FR",
                Microsoft.Graph.Models.DayOfWeekObject.Saturday => "SA",
                Microsoft.Graph.Models.DayOfWeekObject.Sunday => "SU",
                _ => null,
            }).Where(d => d is not null);
            var dayStr = string.Join(",", days);
            if (!string.IsNullOrEmpty(dayStr))
                parts.Add($"BYDAY={dayStr}");
        }

        if (pattern.DayOfMonth is > 0 &&
            pattern.Type is Microsoft.Graph.Models.RecurrencePatternType.AbsoluteMonthly
                or Microsoft.Graph.Models.RecurrencePatternType.AbsoluteYearly)
        {
            parts.Add($"BYMONTHDAY={pattern.DayOfMonth}");
        }

        if (pattern.Month is > 0 &&
            pattern.Type is Microsoft.Graph.Models.RecurrencePatternType.AbsoluteYearly
                or Microsoft.Graph.Models.RecurrencePatternType.RelativeYearly)
        {
            parts.Add($"BYMONTH={pattern.Month}");
        }

        if (recurrence.Range is not null)
        {
            switch (recurrence.Range.Type)
            {
                case Microsoft.Graph.Models.RecurrenceRangeType.Numbered when recurrence.Range.NumberOfOccurrences is > 0:
                    parts.Add($"COUNT={recurrence.Range.NumberOfOccurrences}");
                    break;
                case Microsoft.Graph.Models.RecurrenceRangeType.EndDate when recurrence.Range.EndDate is not null:
                    var ed = recurrence.Range.EndDate.Value;
                    parts.Add($"UNTIL={ed.Year:D4}{ed.Month:D2}{ed.Day:D2}");
                    break;
            }
        }

        return $"RRULE:{string.Join(";", parts)}";
    }

    internal static GraphPatternedRecurrence? ConvertRRuleToRecurrence(string rrule)
    {
        if (!rrule.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
            return null;

        var ruleBody = rrule["RRULE:".Length..];
        var parts = ruleBody.Split(';')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);

        if (!parts.TryGetValue("FREQ", out var freq))
            return null;

        var pattern = new GraphRecurrencePattern
        {
            Type = freq.ToUpperInvariant() switch
            {
                "DAILY" => Microsoft.Graph.Models.RecurrencePatternType.Daily,
                "WEEKLY" => Microsoft.Graph.Models.RecurrencePatternType.Weekly,
                "MONTHLY" => Microsoft.Graph.Models.RecurrencePatternType.AbsoluteMonthly,
                "YEARLY" => Microsoft.Graph.Models.RecurrencePatternType.AbsoluteYearly,
                _ => Microsoft.Graph.Models.RecurrencePatternType.Daily,
            },
            Interval = parts.TryGetValue("INTERVAL", out var interval) && int.TryParse(interval, out var iv) ? iv : 1,
        };

        if (parts.TryGetValue("BYDAY", out var byDay))
        {
            pattern.DaysOfWeek = byDay.Split(',')
                .Select(d => d.Trim() switch
                {
                    "MO" => Microsoft.Graph.Models.DayOfWeekObject.Monday,
                    "TU" => Microsoft.Graph.Models.DayOfWeekObject.Tuesday,
                    "WE" => Microsoft.Graph.Models.DayOfWeekObject.Wednesday,
                    "TH" => Microsoft.Graph.Models.DayOfWeekObject.Thursday,
                    "FR" => Microsoft.Graph.Models.DayOfWeekObject.Friday,
                    "SA" => Microsoft.Graph.Models.DayOfWeekObject.Saturday,
                    "SU" => Microsoft.Graph.Models.DayOfWeekObject.Sunday,
                    _ => (Microsoft.Graph.Models.DayOfWeekObject?)null,
                })
                .Where(d => d is not null)
                .ToList();

            if (pattern.Type == Microsoft.Graph.Models.RecurrencePatternType.AbsoluteMonthly)
                pattern.Type = Microsoft.Graph.Models.RecurrencePatternType.RelativeMonthly;
            if (pattern.Type == Microsoft.Graph.Models.RecurrencePatternType.AbsoluteYearly)
                pattern.Type = Microsoft.Graph.Models.RecurrencePatternType.RelativeYearly;
        }

        if (parts.TryGetValue("BYMONTHDAY", out var byMonthDay) && int.TryParse(byMonthDay, out var dom))
            pattern.DayOfMonth = dom;

        if (parts.TryGetValue("BYMONTH", out var byMonth) && int.TryParse(byMonth, out var month))
            pattern.Month = month;

        var range = new GraphRecurrenceRange { Type = Microsoft.Graph.Models.RecurrenceRangeType.NoEnd };

        if (parts.TryGetValue("COUNT", out var count) && int.TryParse(count, out var cnt))
        {
            range.Type = Microsoft.Graph.Models.RecurrenceRangeType.Numbered;
            range.NumberOfOccurrences = cnt;
        }
        else if (parts.TryGetValue("UNTIL", out var until) && DateOnly.TryParseExact(until, "yyyyMMdd", out var endDate))
        {
            range.Type = Microsoft.Graph.Models.RecurrenceRangeType.EndDate;
            range.EndDate = new Microsoft.Kiota.Abstractions.Date(endDate.Year, endDate.Month, endDate.Day);
        }

        return new GraphPatternedRecurrence
        {
            Pattern = pattern,
            Range = range,
        };
    }
}
