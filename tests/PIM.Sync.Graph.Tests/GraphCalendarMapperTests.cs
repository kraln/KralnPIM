using PIM.Core.Models;
using PIM.Sync.Graph;
using GraphEvent = Microsoft.Graph.Models.Event;
using GraphDateTimeTimeZone = Microsoft.Graph.Models.DateTimeTimeZone;
using GraphAttendee = Microsoft.Graph.Models.Attendee;
using GraphEmailAddress = Microsoft.Graph.Models.EmailAddress;
using GraphItemBody = Microsoft.Graph.Models.ItemBody;
using GraphLocation = Microsoft.Graph.Models.Location;
using GraphPatternedRecurrence = Microsoft.Graph.Models.PatternedRecurrence;
using GraphRecurrencePattern = Microsoft.Graph.Models.RecurrencePattern;
using GraphRecurrenceRange = Microsoft.Graph.Models.RecurrenceRange;

namespace PIM.Sync.Graph.Tests;

public class GraphCalendarMapperTests
{
    private const string AccountId = "test-account";
    private const string CalendarId = "calendar-1";

    [Fact]
    public void ToCalendarEvent_TimedEvent_MapsCorrectly()
    {
        var evt = new GraphEvent
        {
            Id = "evt1",
            Subject = "Team Meeting",
            Body = new GraphItemBody { Content = "Weekly sync" },
            Location = new GraphLocation { DisplayName = "Room 101" },
            IsAllDay = false,
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone
            {
                DateTime = "2024-06-15T10:00:00.0000000",
                TimeZone = "UTC",
            },
            End = new GraphDateTimeTimeZone
            {
                DateTime = "2024-06-15T11:00:00.0000000",
                TimeZone = "UTC",
            },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal("evt1", result.EventId);
        Assert.Equal(AccountId, result.AccountId);
        Assert.Equal(CalendarId, result.CalendarId);
        Assert.Equal("Team Meeting", result.Summary);
        Assert.Equal("Weekly sync", result.Description);
        Assert.Equal("Room 101", result.Location);
        Assert.False(result.IsAllDay);
        Assert.Equal(2024, result.Start.Year);
        Assert.Equal(6, result.Start.Month);
        Assert.Equal(15, result.Start.Day);
        Assert.Equal(10, result.Start.Hour);
        Assert.Equal(2024, result.End.Year);
        Assert.Equal(11, result.End.Hour);
        Assert.Equal(EventStatus.Confirmed, result.Status);
    }

    [Fact]
    public void ToCalendarEvent_ShowAsFree_MapsToFree()
    {
        var evt = new GraphEvent
        {
            Id = "evt-free",
            Subject = "Working elsewhere marker",
            ShowAs = Microsoft.Graph.Models.FreeBusyStatus.Free,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00.0000000", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00.0000000", TimeZone = "UTC" },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(Transparency.Free, result.Transparency);
    }

    [Theory]
    [InlineData(Microsoft.Graph.Models.FreeBusyStatus.Busy)]
    [InlineData(Microsoft.Graph.Models.FreeBusyStatus.Tentative)]
    [InlineData(Microsoft.Graph.Models.FreeBusyStatus.Oof)]
    [InlineData(null)]
    public void ToCalendarEvent_ShowAsNonFree_MapsToBusy(Microsoft.Graph.Models.FreeBusyStatus? showAs)
    {
        var evt = new GraphEvent
        {
            Id = "evt-busy",
            Subject = "Meeting",
            ShowAs = showAs,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00.0000000", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00.0000000", TimeZone = "UTC" },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(Transparency.Busy, result.Transparency);
    }

    [Fact]
    public void ToCalendarEvent_AllDayEvent_MapsCorrectly()
    {
        var evt = new GraphEvent
        {
            Id = "evt2",
            Subject = "Holiday",
            IsAllDay = true,
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone
            {
                DateTime = "2024-12-25T00:00:00.0000000",
                TimeZone = "UTC",
            },
            End = new GraphDateTimeTimeZone
            {
                DateTime = "2024-12-26T00:00:00.0000000",
                TimeZone = "UTC",
            },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.True(result.IsAllDay);
        Assert.Equal(2024, result.Start.Year);
        Assert.Equal(12, result.Start.Month);
        Assert.Equal(25, result.Start.Day);
        Assert.Equal(26, result.End.Day);
    }

    [Fact]
    public void ToCalendarEvent_CancelledEvent_MapsToCancelled()
    {
        var evt = new GraphEvent
        {
            Id = "evt3",
            Subject = "Cancelled Meeting",
            IsCancelled = true,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "UTC" },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(EventStatus.Cancelled, result.Status);
    }

    [Fact]
    public void ToCalendarEvent_TentativeResponse_MapsTentative()
    {
        var evt = new GraphEvent
        {
            Id = "evt4",
            Subject = "Maybe Meeting",
            IsCancelled = false,
            ResponseStatus = new Microsoft.Graph.Models.ResponseStatus
            {
                Response = Microsoft.Graph.Models.ResponseType.TentativelyAccepted,
            },
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "UTC" },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(EventStatus.Tentative, result.Status);
    }

    [Fact]
    public void ToCalendarEvent_WithAttendees_ExtractsEmails()
    {
        var evt = new GraphEvent
        {
            Id = "evt5",
            Subject = "Group Meeting",
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "UTC" },
            Attendees =
            [
                new GraphAttendee { EmailAddress = new GraphEmailAddress { Address = "alice@example.com" } },
                new GraphAttendee { EmailAddress = new GraphEmailAddress { Address = "bob@example.com" } },
                new GraphAttendee { EmailAddress = new GraphEmailAddress { Address = "" } },
            ],
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(2, result.Invitees.Count);
        Assert.Contains("alice@example.com", result.Invitees);
        Assert.Contains("bob@example.com", result.Invitees);
    }

    [Fact]
    public void ToCalendarEvent_NullAttendees_EmptyList()
    {
        var evt = new GraphEvent
        {
            Id = "evt6",
            Subject = "Solo Event",
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "UTC" },
            Attendees = null,
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Empty(result.Invitees);
    }

    [Fact]
    public void ToCalendarEvent_WeeklyRecurrence_ConvertsToRRule()
    {
        var evt = new GraphEvent
        {
            Id = "evt7",
            Subject = "Weekly Standup",
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:15:00", TimeZone = "UTC" },
            Recurrence = new GraphPatternedRecurrence
            {
                Pattern = new GraphRecurrencePattern
                {
                    Type = Microsoft.Graph.Models.RecurrencePatternType.Weekly,
                    Interval = 1,
                    DaysOfWeek = [Microsoft.Graph.Models.DayOfWeekObject.Monday],
                },
                Range = new GraphRecurrenceRange
                {
                    Type = Microsoft.Graph.Models.RecurrenceRangeType.NoEnd,
                },
            },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.NotNull(result.RecurrenceRule);
        Assert.Contains("FREQ=WEEKLY", result.RecurrenceRule);
        Assert.Contains("BYDAY=MO", result.RecurrenceRule);
    }

    [Fact]
    public void ToCalendarEvent_DailyRecurrenceWithCount_ConvertsToRRule()
    {
        var evt = new GraphEvent
        {
            Id = "evt8",
            Subject = "Daily Standup",
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T09:00:00", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T09:15:00", TimeZone = "UTC" },
            Recurrence = new GraphPatternedRecurrence
            {
                Pattern = new GraphRecurrencePattern
                {
                    Type = Microsoft.Graph.Models.RecurrencePatternType.Daily,
                    Interval = 1,
                },
                Range = new GraphRecurrenceRange
                {
                    Type = Microsoft.Graph.Models.RecurrenceRangeType.Numbered,
                    NumberOfOccurrences = 10,
                },
            },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.NotNull(result.RecurrenceRule);
        Assert.Contains("FREQ=DAILY", result.RecurrenceRule);
        Assert.Contains("COUNT=10", result.RecurrenceRule);
    }

    [Fact]
    public void ToCalendarEvent_NoRecurrence_NullRule()
    {
        var evt = new GraphEvent
        {
            Id = "evt9",
            Subject = "One-off",
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "UTC" },
            Recurrence = null,
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Null(result.RecurrenceRule);
    }

    [Fact]
    public void ToCalendarEvent_NullDescription_MapsAsNull()
    {
        var evt = new GraphEvent
        {
            Id = "evt10",
            Subject = "No Description",
            Body = null,
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "UTC" },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Null(result.Description);
    }

    [Fact]
    public void ToCalendarEvent_NullLocation_MapsAsNull()
    {
        var evt = new GraphEvent
        {
            Id = "evt11",
            Subject = "No Location",
            Location = null,
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "UTC" },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Null(result.Location);
    }

    // --- ParseTimeZone edge cases ---

    [Fact]
    public void ToCalendarEvent_EmptyTimeZone_FallsBackToUtc()
    {
        var evt = new GraphEvent
        {
            Id = "tz1",
            Subject = "Empty TZ",
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "" },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(TimeSpan.Zero, result.Start.Offset);
        Assert.Equal(10, result.Start.Hour);
        Assert.Equal(TimeSpan.Zero, result.End.Offset);
    }

    [Fact]
    public void ToCalendarEvent_NullTimeZone_FallsBackToUtc()
    {
        var evt = new GraphEvent
        {
            Id = "tz2",
            Subject = "Null TZ",
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = null },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = null },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(TimeSpan.Zero, result.Start.Offset);
        Assert.Equal(10, result.Start.Hour);
    }

    [Fact]
    public void ToCalendarEvent_InvalidTimeZone_FallsBackToUtc()
    {
        var evt = new GraphEvent
        {
            Id = "tz3",
            Subject = "Invalid TZ",
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "Not/A/Real/Timezone" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "Not/A/Real/Timezone" },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(TimeSpan.Zero, result.Start.Offset);
        Assert.Equal(10, result.Start.Hour);
        Assert.Equal(TimeSpan.Zero, result.End.Offset);
    }

    // --- Implicit all-day detection ---

    [Fact]
    public void ToCalendarEvent_MidnightToMidnight_DetectedAsAllDay()
    {
        var evt = new GraphEvent
        {
            Id = "ad1",
            Subject = "Implicit All Day",
            IsAllDay = false,
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone
            {
                DateTime = "2024-06-15T00:00:00.0000000",
                TimeZone = "UTC",
            },
            End = new GraphDateTimeTimeZone
            {
                DateTime = "2024-06-16T00:00:00.0000000",
                TimeZone = "UTC",
            },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.True(result.IsAllDay);
        Assert.Equal(15, result.Start.Day);
        Assert.Equal(16, result.End.Day);
    }

    // --- Null start/end edge cases ---

    [Fact]
    public void ToCalendarEvent_NullStart_ReturnsMinValue()
    {
        var evt = new GraphEvent
        {
            Id = "ns1",
            Subject = "No Start",
            IsCancelled = false,
            Start = null,
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "UTC" },
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(DateTimeOffset.MinValue, result.Start);
    }

    [Fact]
    public void ToCalendarEvent_NullEnd_ReturnsMinValue()
    {
        var evt = new GraphEvent
        {
            Id = "ne1",
            Subject = "No End",
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "UTC" },
            End = null,
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Equal(DateTimeOffset.MinValue, result.End);
    }

    // --- Empty attendees list ---

    [Fact]
    public void ToCalendarEvent_EmptyAttendeesList_EmptyInvitees()
    {
        var evt = new GraphEvent
        {
            Id = "ea1",
            Subject = "No Attendees",
            IsCancelled = false,
            Start = new GraphDateTimeTimeZone { DateTime = "2024-06-15T10:00:00", TimeZone = "UTC" },
            End = new GraphDateTimeTimeZone { DateTime = "2024-06-15T11:00:00", TimeZone = "UTC" },
            Attendees = [],
        };

        var result = GraphCalendarMapper.ToCalendarEvent(evt, AccountId, CalendarId);

        Assert.Empty(result.Invitees);
    }

    // --- RRULE conversion: RelativeMonthly ---

    [Fact]
    public void ConvertRecurrenceToRRule_RelativeMonthly_ProducesMonthlyWithByDay()
    {
        var recurrence = new GraphPatternedRecurrence
        {
            Pattern = new GraphRecurrencePattern
            {
                Type = Microsoft.Graph.Models.RecurrencePatternType.RelativeMonthly,
                Interval = 1,
                DaysOfWeek = [Microsoft.Graph.Models.DayOfWeekObject.Monday],
            },
            Range = new GraphRecurrenceRange
            {
                Type = Microsoft.Graph.Models.RecurrenceRangeType.NoEnd,
            },
        };

        var result = GraphCalendarMapper.ConvertRecurrenceToRRule(recurrence);

        Assert.NotNull(result);
        Assert.Contains("FREQ=MONTHLY", result);
        Assert.Contains("BYDAY=MO", result);
        Assert.DoesNotContain("BYMONTHDAY", result);
    }

    [Fact]
    public void ConvertRRuleToRecurrence_MonthlyWithByDay_ProducesRelativeMonthly()
    {
        var result = GraphCalendarMapper.ConvertRRuleToRecurrence("RRULE:FREQ=MONTHLY;BYDAY=MO");

        Assert.NotNull(result);
        Assert.Equal(Microsoft.Graph.Models.RecurrencePatternType.RelativeMonthly,
            result.Pattern?.Type);
        Assert.Contains(Microsoft.Graph.Models.DayOfWeekObject.Monday,
            result.Pattern!.DaysOfWeek!);
    }

    // --- RRULE conversion: UNTIL date ---

    [Fact]
    public void ConvertRecurrenceToRRule_EndDateRange_ProducesUntil()
    {
        var recurrence = new GraphPatternedRecurrence
        {
            Pattern = new GraphRecurrencePattern
            {
                Type = Microsoft.Graph.Models.RecurrencePatternType.Daily,
                Interval = 1,
            },
            Range = new GraphRecurrenceRange
            {
                Type = Microsoft.Graph.Models.RecurrenceRangeType.EndDate,
                EndDate = new Microsoft.Kiota.Abstractions.Date(2024, 12, 31),
            },
        };

        var result = GraphCalendarMapper.ConvertRecurrenceToRRule(recurrence);

        Assert.NotNull(result);
        Assert.Contains("FREQ=DAILY", result);
        Assert.Contains("UNTIL=20241231", result);
    }

    [Fact]
    public void ConvertRRuleToRecurrence_WithUntil_ProducesEndDateRange()
    {
        var result = GraphCalendarMapper.ConvertRRuleToRecurrence("RRULE:FREQ=DAILY;UNTIL=20241231");

        Assert.NotNull(result);
        Assert.Equal(Microsoft.Graph.Models.RecurrenceRangeType.EndDate, result.Range?.Type);
        Assert.NotNull(result.Range?.EndDate);
        var ed = result.Range!.EndDate!.Value;
        Assert.Equal(2024, ed.Year);
        Assert.Equal(12, ed.Month);
        Assert.Equal(31, ed.Day);
    }

    [Fact]
    public void ConvertRRuleToRecurrence_InvalidUntilFormat_FallsBackToNoEnd()
    {
        var result = GraphCalendarMapper.ConvertRRuleToRecurrence("RRULE:FREQ=DAILY;UNTIL=not-a-date");

        Assert.NotNull(result);
        Assert.Equal(Microsoft.Graph.Models.RecurrenceRangeType.NoEnd, result.Range?.Type);
    }

    // --- RRULE conversion: AbsoluteYearly with BYMONTH ---

    [Fact]
    public void ConvertRecurrenceToRRule_AbsoluteYearly_ProducesYearlyWithByMonth()
    {
        var recurrence = new GraphPatternedRecurrence
        {
            Pattern = new GraphRecurrencePattern
            {
                Type = Microsoft.Graph.Models.RecurrencePatternType.AbsoluteYearly,
                Interval = 1,
                DayOfMonth = 25,
                Month = 12,
            },
            Range = new GraphRecurrenceRange
            {
                Type = Microsoft.Graph.Models.RecurrenceRangeType.NoEnd,
            },
        };

        var result = GraphCalendarMapper.ConvertRecurrenceToRRule(recurrence);

        Assert.NotNull(result);
        Assert.Contains("FREQ=YEARLY", result);
        Assert.Contains("BYMONTHDAY=25", result);
        Assert.Contains("BYMONTH=12", result);
    }

    // --- RRULE conversion: RelativeYearly ---

    [Fact]
    public void ConvertRecurrenceToRRule_RelativeYearly_ProducesYearlyWithByDayAndMonth()
    {
        var recurrence = new GraphPatternedRecurrence
        {
            Pattern = new GraphRecurrencePattern
            {
                Type = Microsoft.Graph.Models.RecurrencePatternType.RelativeYearly,
                Interval = 1,
                DaysOfWeek = [Microsoft.Graph.Models.DayOfWeekObject.Thursday],
                Month = 11,
            },
            Range = new GraphRecurrenceRange
            {
                Type = Microsoft.Graph.Models.RecurrenceRangeType.NoEnd,
            },
        };

        var result = GraphCalendarMapper.ConvertRecurrenceToRRule(recurrence);

        Assert.NotNull(result);
        Assert.Contains("FREQ=YEARLY", result);
        Assert.Contains("BYDAY=TH", result);
        Assert.Contains("BYMONTH=11", result);
    }

    [Fact]
    public void ConvertRRuleToRecurrence_YearlyWithByDayAndMonth_ProducesRelativeYearly()
    {
        var result = GraphCalendarMapper.ConvertRRuleToRecurrence("RRULE:FREQ=YEARLY;BYDAY=TH;BYMONTH=11");

        Assert.NotNull(result);
        Assert.Equal(Microsoft.Graph.Models.RecurrencePatternType.RelativeYearly,
            result.Pattern?.Type);
        Assert.Contains(Microsoft.Graph.Models.DayOfWeekObject.Thursday,
            result.Pattern!.DaysOfWeek!);
        Assert.Equal(11, result.Pattern.Month);
    }

    // --- RRULE conversion: Interval handling ---

    [Fact]
    public void ConvertRecurrenceToRRule_IntervalGreaterThanOne_IncludesInterval()
    {
        var recurrence = new GraphPatternedRecurrence
        {
            Pattern = new GraphRecurrencePattern
            {
                Type = Microsoft.Graph.Models.RecurrencePatternType.Daily,
                Interval = 3,
            },
            Range = new GraphRecurrenceRange
            {
                Type = Microsoft.Graph.Models.RecurrenceRangeType.NoEnd,
            },
        };

        var result = GraphCalendarMapper.ConvertRecurrenceToRRule(recurrence);

        Assert.NotNull(result);
        Assert.Contains("INTERVAL=3", result);
    }

    [Fact]
    public void ConvertRecurrenceToRRule_IntervalOne_OmitsInterval()
    {
        var recurrence = new GraphPatternedRecurrence
        {
            Pattern = new GraphRecurrencePattern
            {
                Type = Microsoft.Graph.Models.RecurrencePatternType.Daily,
                Interval = 1,
            },
            Range = new GraphRecurrenceRange
            {
                Type = Microsoft.Graph.Models.RecurrenceRangeType.NoEnd,
            },
        };

        var result = GraphCalendarMapper.ConvertRecurrenceToRRule(recurrence);

        Assert.NotNull(result);
        Assert.DoesNotContain("INTERVAL", result);
    }

    // --- RRULE conversion: BYMONTHDAY round-trip ---

    [Fact]
    public void ConvertRRuleToRecurrence_WithByMonthDay_SetsDayOfMonth()
    {
        var result = GraphCalendarMapper.ConvertRRuleToRecurrence("RRULE:FREQ=MONTHLY;BYMONTHDAY=15");

        Assert.NotNull(result);
        Assert.Equal(Microsoft.Graph.Models.RecurrencePatternType.AbsoluteMonthly,
            result.Pattern?.Type);
        Assert.Equal(15, result.Pattern?.DayOfMonth);
    }

    // --- RRULE conversion: null/missing pattern ---

    [Fact]
    public void ConvertRecurrenceToRRule_NullPattern_ReturnsNull()
    {
        var recurrence = new GraphPatternedRecurrence { Pattern = null };

        var result = GraphCalendarMapper.ConvertRecurrenceToRRule(recurrence);

        Assert.Null(result);
    }

    [Fact]
    public void ConvertRRuleToRecurrence_NoFreq_ReturnsNull()
    {
        var result = GraphCalendarMapper.ConvertRRuleToRecurrence("RRULE:INTERVAL=2");

        Assert.Null(result);
    }

    // --- ToGraphEvent: body and location mapping ---

    [Fact]
    public void ToGraphEvent_WithDescription_SetsBodyContent()
    {
        var evt = new CalendarEvent(
            EventId: "bd1",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "Has Desc",
            Description: "Meeting notes here",
            Start: new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero),
            End: new DateTimeOffset(2024, 6, 15, 11, 0, 0, TimeSpan.Zero),
            IsAllDay: false,
            Location: "Room 5",
            Invitees: [],
            RecurrenceRule: null,
            Status: EventStatus.Confirmed
        );

        var result = GraphCalendarMapper.ToGraphEvent(evt);

        Assert.NotNull(result.Body);
        Assert.Equal("Meeting notes here", result.Body.Content);
        Assert.Equal(Microsoft.Graph.Models.BodyType.Text, result.Body.ContentType);
        Assert.NotNull(result.Location);
        Assert.Equal("Room 5", result.Location.DisplayName);
    }

    // --- ToGraphEvent tests ---

    [Fact]
    public void ToGraphEvent_TimedEvent_MapsCorrectly()
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

        var result = GraphCalendarMapper.ToGraphEvent(evt);

        Assert.Equal("evt1", result.Id);
        Assert.Equal("Meeting", result.Subject);
        Assert.Equal("Notes", result.Body?.Content);
        Assert.Equal("Office", result.Location?.DisplayName);
        Assert.False(result.IsAllDay);
        Assert.NotNull(result.Start);
        Assert.Equal("UTC", result.Start.TimeZone);
        Assert.NotNull(result.End);
        Assert.Single(result.Attendees!);
        Assert.Equal("alice@example.com", result.Attendees![0].EmailAddress?.Address);
    }

    [Fact]
    public void ToGraphEvent_AllDayEvent_SetsAllDayFormat()
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

        var result = GraphCalendarMapper.ToGraphEvent(evt);

        Assert.True(result.IsAllDay);
        Assert.Contains("2024-12-25", result.Start!.DateTime);
        Assert.Contains("2024-12-26", result.End!.DateTime);
    }

    [Fact]
    public void ToGraphEvent_NoInvitees_NullAttendees()
    {
        var evt = new CalendarEvent(
            EventId: "evt3",
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

        var result = GraphCalendarMapper.ToGraphEvent(evt);

        Assert.Null(result.Attendees);
    }

    [Fact]
    public void ToGraphEvent_WithRecurrence_SetsRecurrencePattern()
    {
        var evt = new CalendarEvent(
            EventId: "evt4",
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

        var result = GraphCalendarMapper.ToGraphEvent(evt);

        Assert.NotNull(result.Recurrence);
        Assert.Equal(Microsoft.Graph.Models.RecurrencePatternType.Weekly,
            result.Recurrence.Pattern?.Type);
        Assert.Contains(Microsoft.Graph.Models.DayOfWeekObject.Monday,
            result.Recurrence.Pattern!.DaysOfWeek!);
    }

    [Fact]
    public void ToGraphEvent_NullDescription_NullBody()
    {
        var evt = new CalendarEvent(
            EventId: "evt5",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "No Desc",
            Description: null,
            Start: DateTimeOffset.UtcNow,
            End: DateTimeOffset.UtcNow.AddHours(1),
            IsAllDay: false,
            Location: null,
            Invitees: [],
            RecurrenceRule: null,
            Status: EventStatus.Confirmed
        );

        var result = GraphCalendarMapper.ToGraphEvent(evt);

        Assert.Null(result.Body);
    }

    [Fact]
    public void ToGraphEvent_NullLocation_NullLocation()
    {
        var evt = new CalendarEvent(
            EventId: "evt6",
            AccountId: AccountId,
            CalendarId: CalendarId,
            Summary: "No Loc",
            Description: null,
            Start: DateTimeOffset.UtcNow,
            End: DateTimeOffset.UtcNow.AddHours(1),
            IsAllDay: false,
            Location: null,
            Invitees: [],
            RecurrenceRule: null,
            Status: EventStatus.Confirmed
        );

        var result = GraphCalendarMapper.ToGraphEvent(evt);

        Assert.Null(result.Location);
    }

    // --- Recurrence conversion tests ---

    [Fact]
    public void ConvertRecurrenceToRRule_WeeklyMondayFriday_ProducesCorrectRRule()
    {
        var recurrence = new GraphPatternedRecurrence
        {
            Pattern = new GraphRecurrencePattern
            {
                Type = Microsoft.Graph.Models.RecurrencePatternType.Weekly,
                Interval = 1,
                DaysOfWeek =
                [
                    Microsoft.Graph.Models.DayOfWeekObject.Monday,
                    Microsoft.Graph.Models.DayOfWeekObject.Friday,
                ],
            },
            Range = new GraphRecurrenceRange
            {
                Type = Microsoft.Graph.Models.RecurrenceRangeType.NoEnd,
            },
        };

        var result = GraphCalendarMapper.ConvertRecurrenceToRRule(recurrence);

        Assert.NotNull(result);
        Assert.StartsWith("RRULE:", result);
        Assert.Contains("FREQ=WEEKLY", result);
        Assert.Contains("BYDAY=MO,FR", result);
    }

    [Fact]
    public void ConvertRecurrenceToRRule_MonthlyWithDayOfMonth_ProducesCorrectRRule()
    {
        var recurrence = new GraphPatternedRecurrence
        {
            Pattern = new GraphRecurrencePattern
            {
                Type = Microsoft.Graph.Models.RecurrencePatternType.AbsoluteMonthly,
                Interval = 1,
                DayOfMonth = 15,
            },
            Range = new GraphRecurrenceRange
            {
                Type = Microsoft.Graph.Models.RecurrenceRangeType.NoEnd,
            },
        };

        var result = GraphCalendarMapper.ConvertRecurrenceToRRule(recurrence);

        Assert.NotNull(result);
        Assert.Contains("FREQ=MONTHLY", result);
        Assert.Contains("BYMONTHDAY=15", result);
    }

    [Fact]
    public void ConvertRecurrenceToRRule_Null_ReturnsNull()
    {
        var result = GraphCalendarMapper.ConvertRecurrenceToRRule(null);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertRRuleToRecurrence_WeeklyMonday_ProducesCorrectPattern()
    {
        var result = GraphCalendarMapper.ConvertRRuleToRecurrence("RRULE:FREQ=WEEKLY;BYDAY=MO");

        Assert.NotNull(result);
        Assert.Equal(Microsoft.Graph.Models.RecurrencePatternType.Weekly, result.Pattern?.Type);
        Assert.Contains(Microsoft.Graph.Models.DayOfWeekObject.Monday, result.Pattern!.DaysOfWeek!);
    }

    [Fact]
    public void ConvertRRuleToRecurrence_DailyWithCount_ProducesCorrectRange()
    {
        var result = GraphCalendarMapper.ConvertRRuleToRecurrence("RRULE:FREQ=DAILY;COUNT=5");

        Assert.NotNull(result);
        Assert.Equal(Microsoft.Graph.Models.RecurrencePatternType.Daily, result.Pattern?.Type);
        Assert.Equal(Microsoft.Graph.Models.RecurrenceRangeType.Numbered, result.Range?.Type);
        Assert.Equal(5, result.Range?.NumberOfOccurrences);
    }

    [Fact]
    public void ConvertRRuleToRecurrence_InvalidInput_ReturnsNull()
    {
        var result = GraphCalendarMapper.ConvertRRuleToRecurrence("NOT_AN_RRULE");
        Assert.Null(result);
    }

    [Fact]
    public void ConvertRRuleToRecurrence_WithInterval_SetsInterval()
    {
        var result = GraphCalendarMapper.ConvertRRuleToRecurrence("RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=TU,TH");

        Assert.NotNull(result);
        Assert.Equal(2, result.Pattern?.Interval);
        Assert.Equal(2, result.Pattern?.DaysOfWeek?.Count);
    }

    [Fact]
    public void RRuleRoundTrip_WeeklyPattern_PreservesData()
    {
        var original = new GraphPatternedRecurrence
        {
            Pattern = new GraphRecurrencePattern
            {
                Type = Microsoft.Graph.Models.RecurrencePatternType.Weekly,
                Interval = 2,
                DaysOfWeek =
                [
                    Microsoft.Graph.Models.DayOfWeekObject.Monday,
                    Microsoft.Graph.Models.DayOfWeekObject.Wednesday,
                ],
            },
            Range = new GraphRecurrenceRange
            {
                Type = Microsoft.Graph.Models.RecurrenceRangeType.Numbered,
                NumberOfOccurrences = 10,
            },
        };

        var rrule = GraphCalendarMapper.ConvertRecurrenceToRRule(original);
        Assert.NotNull(rrule);

        var roundTripped = GraphCalendarMapper.ConvertRRuleToRecurrence(rrule);
        Assert.NotNull(roundTripped);

        Assert.Equal(original.Pattern.Type, roundTripped.Pattern?.Type);
        Assert.Equal(original.Pattern.Interval, roundTripped.Pattern?.Interval);
        Assert.Equal(original.Pattern.DaysOfWeek?.Count, roundTripped.Pattern?.DaysOfWeek?.Count);
        Assert.Equal(original.Range.NumberOfOccurrences, roundTripped.Range?.NumberOfOccurrences);
    }

    [Fact]
    public void EventRoundTrip_TimedEvent_PreservesKey()
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
            RecurrenceRule: null,
            Status: EventStatus.Confirmed
        );

        var graphEvent = GraphCalendarMapper.ToGraphEvent(original);
        var roundTripped = GraphCalendarMapper.ToCalendarEvent(graphEvent, AccountId, CalendarId);

        Assert.Equal(original.EventId, roundTripped.EventId);
        Assert.Equal(original.Summary, roundTripped.Summary);
        Assert.Equal(original.Description, roundTripped.Description);
        Assert.Equal(original.Start, roundTripped.Start);
        Assert.Equal(original.End, roundTripped.End);
        Assert.Equal(original.IsAllDay, roundTripped.IsAllDay);
        Assert.Equal(original.Location, roundTripped.Location);
        Assert.Equal(original.Invitees, roundTripped.Invitees);
    }
}
