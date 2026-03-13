using PIM.Core.Models;
using PIM.Tui.Views;

namespace PIM.Tui.Tests.Views;

public class AgendaListViewTests
{
    private static readonly DateTime Today = new(2026, 3, 13);

    private static CalendarEvent MakeEvent(
        string summary = "Test Event",
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        bool isAllDay = false,
        string accountId = "acc-1")
    {
        var s = start ?? new DateTimeOffset(2026, 3, 13, 10, 0, 0, TimeSpan.Zero);
        var e = end ?? s.AddHours(1);
        return new CalendarEvent("evt-1", accountId, "cal-1", summary, null,
            s, e, isAllDay, null, [], null, EventStatus.Confirmed);
    }

    [Fact]
    public void BuildRows_EmptyList_ReturnsTodayHeader()
    {
        var rows = AgendaRowBuilder.BuildRows([], Today);

        Assert.Single(rows);
        Assert.Equal(AgendaRowKind.DayHeader, rows[0].Kind);
        Assert.Contains("Today", rows[0].Text);
    }

    [Fact]
    public void BuildRows_SingleTimedEventToday_HeaderAndEvent()
    {
        var evt = MakeEvent("Standup", new DateTimeOffset(2026, 3, 13, 9, 30, 0, TimeSpan.Zero));
        var rows = AgendaRowBuilder.BuildRows([evt], Today);

        Assert.Equal(2, rows.Count);
        Assert.Equal(AgendaRowKind.DayHeader, rows[0].Kind);
        Assert.Contains("Today", rows[0].Text);
        Assert.Equal(AgendaRowKind.Event, rows[1].Kind);
        Assert.Contains("Standup", rows[1].Text);
        Assert.Contains(":", rows[1].Text); // time format HH:mm
    }

    [Fact]
    public void BuildRows_AllDayEvent_FormatsCorrectly()
    {
        var evt = MakeEvent("Birthday", isAllDay: true);
        var rows = AgendaRowBuilder.BuildRows([evt], Today);

        var eventRow = rows.First(r => r.Kind == AgendaRowKind.Event);
        Assert.StartsWith("All day", eventRow.Text);
        Assert.Contains("Birthday", eventRow.Text);
    }

    [Fact]
    public void BuildRows_PastMultiDayEvent_ClampedToToday()
    {
        // Event started March 9, ends March 15 — should show under "Today" not "Mar 9"
        var evt = MakeEvent("Conference",
            start: new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero),
            end: new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
            isAllDay: true);

        var rows = AgendaRowBuilder.BuildRows([evt], Today);

        var header = rows.First(r => r.Kind == AgendaRowKind.DayHeader);
        Assert.Contains("Today", header.Text);
        // Should NOT contain "Mar 9"
        Assert.DoesNotContain("Mar 9", header.Text);
    }

    [Fact]
    public void BuildRows_EventsAcrossMultipleDays_SeparateHeaders()
    {
        var evt1 = MakeEvent("Today Event",
            start: new DateTimeOffset(2026, 3, 13, 10, 0, 0, TimeSpan.Zero));
        var evt2 = MakeEvent("Tomorrow Event",
            start: new DateTimeOffset(2026, 3, 14, 14, 0, 0, TimeSpan.Zero));

        var rows = AgendaRowBuilder.BuildRows([evt1, evt2], Today);

        var headers = rows.Where(r => r.Kind == AgendaRowKind.DayHeader).ToList();
        Assert.Equal(2, headers.Count);
        Assert.Contains("Today", headers[0].Text);
        Assert.Contains("Mar 14", headers[1].Text);

        // Blank separator between day groups
        var blanks = rows.Where(r => r.Kind == AgendaRowKind.Blank).ToList();
        Assert.Single(blanks);
    }

    [Fact]
    public void BuildRows_MultipleEventsSameDay_GroupedUnderOneHeader()
    {
        var evt1 = MakeEvent("Meeting 1",
            start: new DateTimeOffset(2026, 3, 13, 9, 0, 0, TimeSpan.Zero));
        var evt2 = MakeEvent("Meeting 2",
            start: new DateTimeOffset(2026, 3, 13, 11, 0, 0, TimeSpan.Zero));

        var rows = AgendaRowBuilder.BuildRows([evt1, evt2], Today);

        var headers = rows.Where(r => r.Kind == AgendaRowKind.DayHeader).ToList();
        Assert.Single(headers);
        var events = rows.Where(r => r.Kind == AgendaRowKind.Event).ToList();
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void BuildRows_EventRow_HasAccountId()
    {
        var evt = MakeEvent(accountId: "my-gmail");
        var rows = AgendaRowBuilder.BuildRows([evt], Today);

        var eventRow = rows.First(r => r.Kind == AgendaRowKind.Event);
        Assert.Equal("my-gmail", eventRow.AccountId);
    }

    [Fact]
    public void BuildRows_EventRow_HasCalendarEventReference()
    {
        var evt = MakeEvent("Important");
        var rows = AgendaRowBuilder.BuildRows([evt], Today);

        var eventRow = rows.First(r => r.Kind == AgendaRowKind.Event);
        Assert.NotNull(eventRow.CalEvent);
        Assert.Equal("Important", eventRow.CalEvent!.Summary);
    }
}
