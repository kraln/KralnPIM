using PIM.Core.Models;
using PIM.Sync.Google;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;
using GoogleEventDateTime = Google.Apis.Calendar.v3.Data.EventDateTime;
using GoogleEventAttendee = Google.Apis.Calendar.v3.Data.EventAttendee;

namespace PIM.Sync.Google.Tests;

public class CalendarMapperTests
{
    private const string AccountId = "test-account";
    private const string CalendarId = "primary";

    [Fact]
    public void ToCalendarEvent_TimedEvent_MapsCorrectly()
    {
        var start = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.FromHours(2));
        var end = new DateTimeOffset(2024, 6, 15, 11, 0, 0, TimeSpan.FromHours(2));

        var evt = new GoogleEvent
        {
            Id = "evt1",
            Summary = "Team Meeting",
            Description = "Weekly sync",
            Location = "Room 101",
            Status = "confirmed",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = start },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = end },
        };

        var result = CalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal("evt1", result.EventId);
        Assert.Equal(AccountId, result.AccountId);
        Assert.Equal(CalendarId, result.CalendarId);
        Assert.Equal("Team Meeting", result.Summary);
        Assert.Equal("Weekly sync", result.Description);
        Assert.Equal("Room 101", result.Location);
        Assert.Equal(start, result.Start);
        Assert.Equal(end, result.End);
        Assert.False(result.IsAllDay);
        Assert.Equal(EventStatus.Confirmed, result.Status);
    }

    [Fact]
    public void ToCalendarEvent_AllDayEvent_MapsCorrectly()
    {
        var evt = new GoogleEvent
        {
            Id = "evt2",
            Summary = "Holiday",
            Status = "confirmed",
            Start = new GoogleEventDateTime { Date = "2024-12-25" },
            End = new GoogleEventDateTime { Date = "2024-12-26" },
        };

        var result = CalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.True(result.IsAllDay);
        var expectedOffset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024, 12, 25));
        Assert.Equal(new DateTimeOffset(2024, 12, 25, 0, 0, 0, expectedOffset), result.Start);
        Assert.Equal(new DateTimeOffset(2024, 12, 26, 0, 0, 0, expectedOffset), result.End);
    }

    [Fact]
    public void ToCalendarEvent_TentativeStatus_Maps()
    {
        var evt = new GoogleEvent
        {
            Id = "evt3",
            Summary = "Maybe Meeting",
            Status = "tentative",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow.AddHours(1) },
        };

        var result = CalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(EventStatus.Tentative, result.Status);
    }

    [Fact]
    public void ToCalendarEvent_CancelledStatus_Maps()
    {
        var evt = new GoogleEvent
        {
            Id = "evt4",
            Summary = "Cancelled Meeting",
            Status = "cancelled",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow.AddHours(1) },
        };

        var result = CalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(EventStatus.Cancelled, result.Status);
    }

    [Fact]
    public void ToCalendarEvent_WithAttendees_ExtractsEmails()
    {
        var evt = new GoogleEvent
        {
            Id = "evt5",
            Summary = "Group Meeting",
            Status = "confirmed",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow.AddHours(1) },
            Attendees =
            [
                new GoogleEventAttendee { Email = "alice@example.com" },
                new GoogleEventAttendee { Email = "bob@example.com" },
                new GoogleEventAttendee { Email = "" },
            ],
        };

        var result = CalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(2, result.Invitees.Count);
        Assert.Contains("alice@example.com", result.Invitees);
        Assert.Contains("bob@example.com", result.Invitees);
    }

    [Fact]
    public void ToCalendarEvent_WithRecurrence_ExtractsRRule()
    {
        var evt = new GoogleEvent
        {
            Id = "evt6",
            Summary = "Weekly Standup",
            Status = "confirmed",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow.AddMinutes(15) },
            Recurrence = ["RRULE:FREQ=WEEKLY;BYDAY=MO", "EXDATE:20240701T100000Z"],
        };

        var result = CalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal("RRULE:FREQ=WEEKLY;BYDAY=MO", result.RecurrenceRule);
    }

    [Fact]
    public void ToCalendarEvent_NoRecurrence_NullRule()
    {
        var evt = new GoogleEvent
        {
            Id = "evt7",
            Summary = "One-off",
            Status = "confirmed",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow.AddHours(1) },
        };

        var result = CalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Null(result.RecurrenceRule);
    }

    [Fact]
    public void ToCalendarEvent_NullDescription_MapsAsNull()
    {
        var evt = new GoogleEvent
        {
            Id = "evt8",
            Summary = "No description",
            Status = "confirmed",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = DateTimeOffset.UtcNow.AddHours(1) },
        };

        var result = CalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Null(result.Description);
    }

    [Fact]
    public void ToGoogleEvent_TimedEvent_MapsCorrectly()
    {
        var start = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 6, 15, 11, 0, 0, TimeSpan.Zero);

        var evt = new CalendarEvent(
            EventId: "evt1",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Meeting",
            Description: "Notes",
            Start: start,
            End: end,
            IsAllDay: false,
            Location: "Office",
            Invitees: ["alice@example.com"],
            RecurrenceRule: null,
            Status: EventStatus.Confirmed
        );

        var result = CalendarMapper.ToGoogleEvent(evt);

        Assert.Equal("evt1", result.Id);
        Assert.Equal("Meeting", result.Summary);
        Assert.Equal("Notes", result.Description);
        Assert.Equal("Office", result.Location);
        Assert.Equal("confirmed", result.Status);
        Assert.Equal(start, result.Start.DateTimeDateTimeOffset);
        Assert.Equal(end, result.End.DateTimeDateTimeOffset);
        Assert.Null(result.Start.Date);
        Assert.Single(result.Attendees);
        Assert.Equal("alice@example.com", result.Attendees[0].Email);
    }

    [Fact]
    public void ToGoogleEvent_AllDayEvent_UsesDateNotDateTime()
    {
        var start = new DateTimeOffset(2024, 12, 25, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 12, 26, 0, 0, 0, TimeSpan.Zero);

        var evt = new CalendarEvent(
            EventId: "evt2",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Holiday",
            Description: null,
            Start: start,
            End: end,
            IsAllDay: true,
            Location: null,
            Invitees: [],
            RecurrenceRule: null,
            Status: EventStatus.Confirmed
        );

        var result = CalendarMapper.ToGoogleEvent(evt);

        Assert.Equal("2024-12-25", result.Start.Date);
        Assert.Equal("2024-12-26", result.End.Date);
        Assert.Null(result.Start.DateTimeDateTimeOffset);
        Assert.Null(result.End.DateTimeDateTimeOffset);
    }

    [Fact]
    public void ToGoogleEvent_WithRecurrence_SetsRecurrenceList()
    {
        var evt = new CalendarEvent(
            EventId: "evt3",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Weekly",
            Description: null,
            Start: DateTimeOffset.UtcNow,
            End: DateTimeOffset.UtcNow.AddHours(1),
            IsAllDay: false,
            Location: null,
            Invitees: [],
            RecurrenceRule: "RRULE:FREQ=WEEKLY;BYDAY=MO",
            Status: EventStatus.Confirmed
        );

        var result = CalendarMapper.ToGoogleEvent(evt);

        Assert.Single(result.Recurrence);
        Assert.Equal("RRULE:FREQ=WEEKLY;BYDAY=MO", result.Recurrence[0]);
    }

    [Fact]
    public void ToGoogleEvent_NoInvitees_NullAttendees()
    {
        var evt = new CalendarEvent(
            EventId: "evt4",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Solo",
            Description: null,
            Start: DateTimeOffset.UtcNow,
            End: DateTimeOffset.UtcNow.AddHours(1),
            IsAllDay: false,
            Location: null,
            Invitees: [],
            RecurrenceRule: null,
            Status: EventStatus.Tentative
        );

        var result = CalendarMapper.ToGoogleEvent(evt);

        Assert.Null(result.Attendees);
        Assert.Equal("tentative", result.Status);
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

        var googleEvent = CalendarMapper.ToGoogleEvent(original);
        var roundTripped = CalendarMapper.ToCalendarEvent(googleEvent, AccountId, CalendarId);

        Assert.Equal(original.EventId, roundTripped.EventId);
        Assert.Equal(original.Summary, roundTripped.Summary);
        Assert.Equal(original.Description, roundTripped.Description);
        Assert.Equal(original.Start, roundTripped.Start);
        Assert.Equal(original.End, roundTripped.End);
        Assert.Equal(original.IsAllDay, roundTripped.IsAllDay);
        Assert.Equal(original.Location, roundTripped.Location);
        Assert.Equal(original.Status, roundTripped.Status);
        Assert.Equal(original.Invitees, roundTripped.Invitees);
        Assert.Equal(original.RecurrenceRule, roundTripped.RecurrenceRule);
    }
}
