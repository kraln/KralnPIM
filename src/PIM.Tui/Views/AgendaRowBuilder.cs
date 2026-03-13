using PIM.Core.Models;

namespace PIM.Tui.Views;

internal enum AgendaRowKind { DayHeader, Event, Blank }

internal sealed record AgendaRow(AgendaRowKind Kind, string? AccountId, string Text, CalendarEvent? CalEvent);

/// <summary>
/// Pure-logic helper for building agenda rows from calendar events.
/// Separated from AgendaListView to allow unit testing without Terminal.Gui.
/// </summary>
internal static class AgendaRowBuilder
{
    /// <summary>
    /// Builds agenda rows from a list of calendar events, grouping by day.
    /// Events starting before <paramref name="today"/> are clamped to today.
    /// </summary>
    public static List<AgendaRow> BuildRows(List<CalendarEvent> events, DateTime today)
    {
        var rows = new List<AgendaRow>();
        var currentDate = (DateTime?)null;

        foreach (var e in events)
        {
            // Clamp past start dates to today so multi-day events show under "Today"
            var rawDate = e.Start.ToLocalTime().Date;
            var eventDate = rawDate < today ? today : rawDate;
            if (currentDate != eventDate)
            {
                if (rows.Count > 0) rows.Add(new AgendaRow(AgendaRowKind.Blank, null, "", null));
                var dayLabel = eventDate == today
                    ? $"Today - {eventDate:ddd MMM d}"
                    : $"{eventDate:ddd MMM d}";
                rows.Add(new AgendaRow(AgendaRowKind.DayHeader, null, dayLabel, null));
                currentDate = eventDate;
            }

            var text = e.IsAllDay
                ? $"All day  {e.Summary}"
                : $"{e.Start.ToLocalTime():HH:mm}  {e.Summary}";
            rows.Add(new AgendaRow(AgendaRowKind.Event, e.AccountId, text, e));
        }

        if (rows.Count == 0)
            rows.Add(new AgendaRow(AgendaRowKind.DayHeader, null, $"Today - {today:ddd MMM d}", null));

        return rows;
    }
}
