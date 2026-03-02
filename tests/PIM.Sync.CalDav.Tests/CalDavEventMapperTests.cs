using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using PIM.Core.Models;
using PIM.Sync.CalDav;
using ICalEvent = Ical.Net.CalendarComponents.CalendarEvent;
using CalendarEvent = PIM.Core.Models.CalendarEvent;
using EventStatus = PIM.Core.Models.EventStatus;

namespace PIM.Sync.CalDav.Tests;

public class CalDavEventMapperTests
{
    private const string AccountId = "test-account";
    private const string CalendarId = "default";

    [Fact]
    public void ToCalendarEvent_TimedEvent_MapsCorrectly()
    {
        var start = new CalDateTime(2024, 6, 15, 10, 0, 0, "UTC");
        var end = new CalDateTime(2024, 6, 15, 11, 0, 0, "UTC");

        var evt = new ICalEvent
        {
            Uid = "evt1",
            Summary = "Team Meeting",
            Description = "Weekly sync",
            Location = "Room 101",
            Status = "CONFIRMED",
            DtStart = start,
            DtEnd = end,
        };

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal("evt1", result.EventId);
        Assert.Equal(AccountId, result.AccountId);
        Assert.Equal(CalendarId, result.CalendarId);
        Assert.Equal("Team Meeting", result.Summary);
        Assert.Equal("Weekly sync", result.Description);
        Assert.Equal("Room 101", result.Location);
        Assert.Equal(new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero), result.Start);
        Assert.Equal(new DateTimeOffset(2024, 6, 15, 11, 0, 0, TimeSpan.Zero), result.End);
        Assert.False(result.IsAllDay);
        Assert.Equal(EventStatus.Confirmed, result.Status);
    }

    [Fact]
    public void ToCalendarEvent_TimedEventWithTimezone_ConvertsToUtc()
    {
        // US Eastern = UTC-5 (standard) / UTC-4 (daylight)
        var start = new CalDateTime(2024, 6, 15, 10, 0, 0, "America/New_York");
        var end = new CalDateTime(2024, 6, 15, 11, 0, 0, "America/New_York");

        var evt = new ICalEvent
        {
            Uid = "evt-tz",
            Summary = "NYC Meeting",
            DtStart = start,
            DtEnd = end,
        };

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.False(result.IsAllDay);
        // June = EDT (UTC-4), so 10:00 EDT = 14:00 UTC
        Assert.Equal(new DateTimeOffset(2024, 6, 15, 14, 0, 0, TimeSpan.Zero), result.Start);
        Assert.Equal(new DateTimeOffset(2024, 6, 15, 15, 0, 0, TimeSpan.Zero), result.End);
    }

    [Fact]
    public void ToCalendarEvent_AllDayEvent_MapsCorrectly()
    {
        var start = new CalDateTime(2024, 12, 25);
        start.HasTime = false;
        var end = new CalDateTime(2024, 12, 26);
        end.HasTime = false;

        var evt = new ICalEvent
        {
            Uid = "evt2",
            Summary = "Holiday",
            Status = "CONFIRMED",
            DtStart = start,
            DtEnd = end,
        };

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.True(result.IsAllDay);
        Assert.Equal(new DateTimeOffset(2024, 12, 25, 0, 0, 0, TimeSpan.Zero), result.Start);
        Assert.Equal(new DateTimeOffset(2024, 12, 26, 0, 0, 0, TimeSpan.Zero), result.End);
    }

    [Fact]
    public void ToCalendarEvent_TentativeStatus_Maps()
    {
        var evt = new ICalEvent
        {
            Uid = "evt3",
            Summary = "Maybe Meeting",
            Status = "TENTATIVE",
            DtStart = new CalDateTime(2024, 1, 1, 9, 0, 0, "UTC"),
            DtEnd = new CalDateTime(2024, 1, 1, 10, 0, 0, "UTC"),
        };

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(EventStatus.Tentative, result.Status);
    }

    [Fact]
    public void ToCalendarEvent_CancelledStatus_Maps()
    {
        var evt = new ICalEvent
        {
            Uid = "evt4",
            Summary = "Cancelled Meeting",
            Status = "CANCELLED",
            DtStart = new CalDateTime(2024, 1, 1, 9, 0, 0, "UTC"),
            DtEnd = new CalDateTime(2024, 1, 1, 10, 0, 0, "UTC"),
        };

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(EventStatus.Cancelled, result.Status);
    }

    [Fact]
    public void ToCalendarEvent_UnknownStatus_DefaultsToConfirmed()
    {
        var evt = new ICalEvent
        {
            Uid = "evt-unknown",
            Summary = "Some Event",
            DtStart = new CalDateTime(2024, 1, 1, 9, 0, 0, "UTC"),
            DtEnd = new CalDateTime(2024, 1, 1, 10, 0, 0, "UTC"),
        };

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(EventStatus.Confirmed, result.Status);
    }

    [Fact]
    public void ToCalendarEvent_WithAttendees_ExtractsEmails()
    {
        var evt = new ICalEvent
        {
            Uid = "evt5",
            Summary = "Group Meeting",
            DtStart = new CalDateTime(2024, 1, 1, 9, 0, 0, "UTC"),
            DtEnd = new CalDateTime(2024, 1, 1, 10, 0, 0, "UTC"),
        };
        evt.Attendees.Add(new Attendee("mailto:alice@example.com"));
        evt.Attendees.Add(new Attendee("mailto:bob@example.com"));

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(2, result.Invitees.Count);
        Assert.Contains("alice@example.com", result.Invitees);
        Assert.Contains("bob@example.com", result.Invitees);
    }

    [Fact]
    public void ToCalendarEvent_WithRecurrence_ExtractsRRule()
    {
        var evt = new ICalEvent
        {
            Uid = "evt6",
            Summary = "Weekly Standup",
            DtStart = new CalDateTime(2024, 1, 1, 9, 0, 0, "UTC"),
            DtEnd = new CalDateTime(2024, 1, 1, 9, 15, 0, "UTC"),
        };
        evt.RecurrenceRules.Add(new RecurrencePattern("FREQ=WEEKLY;BYDAY=MO"));

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.NotNull(result.RecurrenceRule);
        Assert.StartsWith("RRULE:", result.RecurrenceRule);
        Assert.Contains("FREQ=WEEKLY", result.RecurrenceRule);
        Assert.Contains("BYDAY=MO", result.RecurrenceRule);
    }

    [Fact]
    public void ToCalendarEvent_NoRecurrence_NullRule()
    {
        var evt = new ICalEvent
        {
            Uid = "evt7",
            Summary = "One-off",
            DtStart = new CalDateTime(2024, 1, 1, 9, 0, 0, "UTC"),
            DtEnd = new CalDateTime(2024, 1, 1, 10, 0, 0, "UTC"),
        };

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Null(result.RecurrenceRule);
    }

    [Fact]
    public void ToCalendarEvent_NullDescription_MapsAsNull()
    {
        var evt = new ICalEvent
        {
            Uid = "evt8",
            Summary = "No description",
            DtStart = new CalDateTime(2024, 1, 1, 9, 0, 0, "UTC"),
            DtEnd = new CalDateTime(2024, 1, 1, 10, 0, 0, "UTC"),
        };

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Null(result.Description);
    }

    [Fact]
    public void ToCalendarEvent_NullLocation_MapsAsNull()
    {
        var evt = new ICalEvent
        {
            Uid = "evt9",
            Summary = "Remote",
            DtStart = new CalDateTime(2024, 1, 1, 9, 0, 0, "UTC"),
            DtEnd = new CalDateTime(2024, 1, 1, 10, 0, 0, "UTC"),
        };

        var result = CalDavEventMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Null(result.Location);
    }

    [Fact]
    public void ToICalendar_TimedEvent_ProducesValidVCalendar()
    {
        var evt = new CalendarEvent(
            EventId: "ical1",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Test Meeting",
            Description: "Some notes",
            Start: new DateTimeOffset(2024, 6, 15, 14, 0, 0, TimeSpan.Zero),
            End: new DateTimeOffset(2024, 6, 15, 15, 0, 0, TimeSpan.Zero),
            IsAllDay: false,
            Location: "Office",
            Invitees: ["alice@example.com"],
            RecurrenceRule: null,
            Status: EventStatus.Confirmed
        );

        var ics = CalDavEventMapper.ToICalendar(evt);

        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("END:VEVENT", ics);
        Assert.Contains("END:VCALENDAR", ics);
        Assert.Contains("UID:ical1", ics);
        Assert.Contains("SUMMARY:Test Meeting", ics);
        Assert.Contains("DESCRIPTION:Some notes", ics);
        Assert.Contains("LOCATION:Office", ics);
        Assert.Contains("STATUS:CONFIRMED", ics);
        Assert.Contains("ATTENDEE:mailto:alice@example.com", ics);
    }

    [Fact]
    public void ToICalendar_AllDayEvent_UsesDateValue()
    {
        var evt = new CalendarEvent(
            EventId: "ical2",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Holiday",
            Description: null,
            Start: new DateTimeOffset(2024, 12, 25, 0, 0, 0, TimeSpan.Zero),
            End: new DateTimeOffset(2024, 12, 26, 0, 0, 0, TimeSpan.Zero),
            IsAllDay: true,
            Location: null,
            Invitees: [],
            RecurrenceRule: null,
            Status: EventStatus.Confirmed
        );

        var ics = CalDavEventMapper.ToICalendar(evt);

        Assert.Contains("DTSTART;VALUE=DATE:20241225", ics);
        Assert.Contains("DTEND;VALUE=DATE:20241226", ics);
    }

    [Fact]
    public void ToICalendar_WithRecurrence_IncludesRRule()
    {
        var evt = new CalendarEvent(
            EventId: "ical3",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Weekly",
            Description: null,
            Start: new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero),
            End: new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
            IsAllDay: false,
            Location: null,
            Invitees: [],
            RecurrenceRule: "RRULE:FREQ=WEEKLY;BYDAY=MO",
            Status: EventStatus.Confirmed
        );

        var ics = CalDavEventMapper.ToICalendar(evt);

        Assert.Contains("RRULE:", ics);
        Assert.Contains("FREQ=WEEKLY", ics);
    }

    [Fact]
    public void ToICalendar_TentativeStatus_Maps()
    {
        var evt = new CalendarEvent(
            EventId: "ical4",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Maybe",
            Description: null,
            Start: new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero),
            End: new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
            IsAllDay: false,
            Location: null,
            Invitees: [],
            RecurrenceRule: null,
            Status: EventStatus.Tentative
        );

        var ics = CalDavEventMapper.ToICalendar(evt);

        Assert.Contains("STATUS:TENTATIVE", ics);
    }

    [Fact]
    public void RoundTrip_TimedEvent_PreservesData()
    {
        var original = new CalendarEvent(
            EventId: "rt1",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Round Trip",
            Description: "Test round trip",
            Start: new DateTimeOffset(2024, 3, 15, 14, 0, 0, TimeSpan.Zero),
            End: new DateTimeOffset(2024, 3, 15, 15, 0, 0, TimeSpan.Zero),
            IsAllDay: false,
            Location: "Conference Room",
            Invitees: ["test@example.com"],
            RecurrenceRule: "RRULE:FREQ=DAILY",
            Status: EventStatus.Confirmed
        );

        var ics = CalDavEventMapper.ToICalendar(original);
        var calendar = Calendar.Load(ics);
        var icalEvent = calendar.Events.First();
        var roundTripped = CalDavEventMapper.ToCalendarEvent(icalEvent, AccountId, CalendarId);

        Assert.Equal(original.EventId, roundTripped.EventId);
        Assert.Equal(original.Summary, roundTripped.Summary);
        Assert.Equal(original.Description, roundTripped.Description);
        Assert.Equal(original.Start, roundTripped.Start);
        Assert.Equal(original.End, roundTripped.End);
        Assert.Equal(original.IsAllDay, roundTripped.IsAllDay);
        Assert.Equal(original.Location, roundTripped.Location);
        Assert.Equal(original.Status, roundTripped.Status);
        Assert.Equal(original.Invitees, roundTripped.Invitees);
        Assert.NotNull(roundTripped.RecurrenceRule);
        Assert.Contains("FREQ=DAILY", roundTripped.RecurrenceRule);
    }

    [Fact]
    public void RoundTrip_AllDayEvent_PreservesData()
    {
        var original = new CalendarEvent(
            EventId: "rt2",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "All Day Round Trip",
            Description: null,
            Start: new DateTimeOffset(2024, 12, 25, 0, 0, 0, TimeSpan.Zero),
            End: new DateTimeOffset(2024, 12, 26, 0, 0, 0, TimeSpan.Zero),
            IsAllDay: true,
            Location: null,
            Invitees: [],
            RecurrenceRule: null,
            Status: EventStatus.Confirmed
        );

        var ics = CalDavEventMapper.ToICalendar(original);
        var calendar = Calendar.Load(ics);
        var icalEvent = calendar.Events.First();
        var roundTripped = CalDavEventMapper.ToCalendarEvent(icalEvent, AccountId, CalendarId);

        Assert.Equal(original.EventId, roundTripped.EventId);
        Assert.Equal(original.Summary, roundTripped.Summary);
        Assert.True(roundTripped.IsAllDay);
        Assert.Equal(original.Start, roundTripped.Start);
        Assert.Equal(original.End, roundTripped.End);
    }

    [Fact]
    public void ToICalendar_CanBeParsedByICalNet()
    {
        var evt = new CalendarEvent(
            EventId: "parse1",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Parseable",
            Description: "Description with\nnewlines",
            Start: new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero),
            End: new DateTimeOffset(2024, 6, 15, 11, 0, 0, TimeSpan.Zero),
            IsAllDay: false,
            Location: null,
            Invitees: ["a@b.com", "c@d.com"],
            RecurrenceRule: null,
            Status: EventStatus.Tentative
        );

        var ics = CalDavEventMapper.ToICalendar(evt);
        var calendar = Calendar.Load(ics);

        Assert.Single(calendar.Events);
        var parsed = calendar.Events.First();
        Assert.Equal("parse1", parsed.Uid);
        Assert.Equal("Parseable", parsed.Summary);
        Assert.Equal(2, parsed.Attendees.Count);
    }
}
