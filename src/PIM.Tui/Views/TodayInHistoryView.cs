using PIM.Core.Models;
using PIM.Tui.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace PIM.Tui.Views;

internal sealed class TodayInHistoryView : View
{
    // _rendered is rebuilt from _data + _clock each time SetData/SetClock is called,
    // or lazily on draw when the viewport width changes (for re-wrapping).
    private TihResponse? _data;
    private ClockInfo? _clock;
    private List<TihLine> _rendered = [];
    private int _lastWrappedWidth;
    private int _scrollOffset;

    public TodayInHistoryView()
    {
        CanFocus = true;
        KeyDown += HandleKeyDown;
        MouseEvent += HandleMouseEvent;
    }

    public void SetData(TihResponse? data)
    {
        _data = data;
        RebuildLines();
    }

    public void SetClock(ClockInfo? clock)
    {
        _clock = clock;
        RebuildLines();
    }

    private void RebuildLines()
    {
        var width = Math.Max(10, Viewport.Width);
        _rendered = BuildLines(_data, _clock, width);
        _lastWrappedWidth = width;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, _rendered.Count - Viewport.Height));
        SetNeedsDraw();
    }

    private void HandleKeyDown(object? sender, Key e)
    {
        var pageSize = Math.Max(1, Viewport.Height);
        if (e == Key.CursorDown || e == Key.J)
        {
            Scroll(1);
            e.Handled = true;
        }
        else if (e == Key.CursorUp || e == Key.K)
        {
            Scroll(-1);
            e.Handled = true;
        }
        else if (e == Key.PageDown)
        {
            Scroll(pageSize);
            e.Handled = true;
        }
        else if (e == Key.PageUp)
        {
            Scroll(-pageSize);
            e.Handled = true;
        }
    }

    private void HandleMouseEvent(object? sender, Mouse e)
    {
        if (e.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            Scroll(3);
            e.Handled = true;
        }
        else if (e.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            Scroll(-3);
            e.Handled = true;
        }
        else if (e.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            if (!HasFocus) SetFocus();
            e.Handled = true;
        }
    }

    private void Scroll(int delta)
    {
        var maxOffset = Math.Max(0, _rendered.Count - Viewport.Height);
        var next = Math.Clamp(_scrollOffset + delta, 0, maxOffset);
        if (next == _scrollOffset) return;
        _scrollOffset = next;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var height = Viewport.Height;

        // Re-wrap if viewport width changed
        if (width != _lastWrappedWidth)
        {
            _rendered = BuildLines(_data, _clock, Math.Max(10, width));
            _lastWrappedWidth = width;
            _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, _rendered.Count - height));
        }

        SetAttribute(Sol.Normal);
        for (var r = 0; r < height; r++)
        {
            Move(0, r);
            AddStr(new string(' ', width));
        }

        for (var vi = 0; vi < height; vi++)
        {
            var lineIdx = _scrollOffset + vi;
            if (lineIdx >= _rendered.Count) break;

            var line = _rendered[lineIdx];
            Move(0, vi);

            switch (line.Kind)
            {
                case TihLineKind.Greeting:
                case TihLineKind.GreetingCont:
                    SetAttribute(Sol.Heading);
                    AddStr(line.Text.Length > width ? line.Text[..width] : line.Text);
                    break;

                case TihLineKind.ClockLine:
                    SetAttribute(new GuiAttribute(Sol.Cyan, Sol.Base03));
                    AddStr(line.Text.Length > width ? line.Text[..width] : line.Text);
                    break;

                case TihLineKind.SectionHeader:
                    SetAttribute(Sol.Heading);
                    AddStr(line.Text.Length > width ? line.Text[..width] : line.Text);
                    break;

                case TihLineKind.Entry:
                case TihLineKind.EntryCont:
                    if (line.Kind == TihLineKind.Entry && line.Year is not null)
                    {
                        SetAttribute(new GuiAttribute(Sol.Cyan, Sol.Base03));
                        AddStr(line.Year);
                        SetAttribute(Sol.Normal);
                        var desc = $"  {line.Text}";
                        var remaining = width - line.Year.Length;
                        if (remaining > 0)
                            AddStr(desc.Length > remaining ? desc[..remaining] : desc);
                    }
                    else
                    {
                        // EntryCont or Entry without year — text is already indented
                        SetAttribute(Sol.Normal);
                        AddStr(line.Text.Length > width ? line.Text[..width] : line.Text);
                    }
                    break;

                case TihLineKind.Blank:
                    break;
            }
        }

        return true;
    }

    private static List<TihLine> BuildLines(TihResponse? data, ClockInfo? clock, int width)
    {
        if (data is null)
            return [new TihLine(TihLineKind.Greeting, "Today in History unavailable", null)];

        var lines = new List<TihLine>();

        // Padding line at top
        lines.Add(new TihLine(TihLineKind.Blank, "", null));

        // Build greeting: "Good morning! It's Monday, March 9th 2026."
        var now = DateTimeOffset.Now;
        var greeting = TimeOfDayGreeting(now);
        var dayName = now.ToString("dddd");
        var month = now.ToString("MMMM");
        var dayOrd = Ordinal(now.Day);
        var year = now.Year;
        var weekNum = System.Globalization.ISOWeek.GetWeekOfYear(now.DateTime);
        var dayOfYear = now.DayOfYear;
        var daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
        var daysRemaining = daysInYear - dayOfYear;

        var line1 = $"{greeting} It's {dayName}, {month} {dayOrd} {year}.";
        var line2 = $"It's the {Ordinal(weekNum)} week of the year, " +
                    $"there are {daysRemaining} days left.";

        foreach (var wl in WordWrap(line1, width))
            lines.Add(new TihLine(lines.Count == 0 ? TihLineKind.Greeting : TihLineKind.GreetingCont, wl, null));
        foreach (var wl in WordWrap(line2, width))
            lines.Add(new TihLine(TihLineKind.GreetingCont, wl, null));

        // Clock line — use abbreviation from Label, show each zone's own time
        if (clock is { Zones.Count: > 0 })
        {
            var clockStr = string.Join(", ", clock.Zones.Select(z =>
                $"{Abbreviate(z.Label)}: {z.CurrentTime:HH:mm}"));
            lines.Add(new TihLine(TihLineKind.ClockLine, clockStr, null));
        }

        lines.Add(new TihLine(TihLineKind.Blank, "", null));

        // Holidays
        if (data.Holidays is { Count: > 0 })
        {
            lines.Add(new TihLine(TihLineKind.SectionHeader, "Holidays", null));
            foreach (var h in data.Holidays)
                AddEntryWrapped(lines, h.Name, null, width);
            lines.Add(new TihLine(TihLineKind.Blank, "", null));
        }

        // Birthdays
        if (data.Birthdays is { Count: > 0 })
        {
            lines.Add(new TihLine(TihLineKind.SectionHeader, "Happy Birthday to...", null));
            foreach (var b in data.Birthdays)
                AddEntryWrapped(lines, b.Description, b.Year?.ToString(), width);
            lines.Add(new TihLine(TihLineKind.Blank, "", null));
        }

        // Events
        if (data.Events is { Count: > 0 })
        {
            lines.Add(new TihLine(TihLineKind.SectionHeader, "On this day...", null));
            foreach (var ev in data.Events)
                AddEntryWrapped(lines, ev.Description, ev.Year?.ToString(), width);
            lines.Add(new TihLine(TihLineKind.Blank, "", null));
        }

        // Personal
        if (data.Personal is { Count: > 0 })
        {
            lines.Add(new TihLine(TihLineKind.SectionHeader, "Personal", null));
            foreach (var p in data.Personal)
                AddEntryWrapped(lines, $"[{p.Type}] {p.Description}", null, width);
        }

        return lines;
    }

    /// <summary>
    /// Adds an entry with optional year prefix, wrapping continuation lines
    /// to align with the text start (after "  YYYY  " or "  ").
    /// </summary>
    private static void AddEntryWrapped(List<TihLine> lines, string text, string? year, int width)
    {
        // First line: "YYYY  description" or "  description"
        // indent = columns consumed by the prefix on the first line
        int indent;
        int firstLineAvail;
        if (year is not null)
        {
            // Rendered as: "{year}  {text}" — year in cyan, gap, then text
            indent = year.Length + 2; // year + "  "
            firstLineAvail = width - indent;
        }
        else
        {
            indent = 2; // "  "
            firstLineAvail = width - indent;
        }

        if (firstLineAvail <= 0)
        {
            // Terminal too narrow, just emit truncated
            lines.Add(new TihLine(TihLineKind.Entry, text, year));
            return;
        }

        if (text.Length <= firstLineAvail)
        {
            // Fits on one line
            lines.Add(new TihLine(TihLineKind.Entry, text, year));
            return;
        }

        // Wrap: first line gets firstLineAvail chars, continuations get (width - indent)
        var breakIdx = FindWordBreak(text, firstLineAvail);
        lines.Add(new TihLine(TihLineKind.Entry, text[..breakIdx].TrimEnd(), year));

        var remaining = text[breakIdx..].TrimStart();
        var contAvail = width - indent;
        var pad = new string(' ', indent);

        while (remaining.Length > 0)
        {
            if (remaining.Length <= contAvail)
            {
                lines.Add(new TihLine(TihLineKind.EntryCont, pad + remaining, null));
                break;
            }
            var bi = FindWordBreak(remaining, contAvail);
            lines.Add(new TihLine(TihLineKind.EntryCont, pad + remaining[..bi].TrimEnd(), null));
            remaining = remaining[bi..].TrimStart();
        }
    }

    /// <summary>
    /// Word-wraps a plain text string at the given width.
    /// </summary>
    private static List<string> WordWrap(string text, int width)
    {
        if (width <= 0) return [text];
        var result = new List<string>();
        var remaining = text;
        while (remaining.Length > width)
        {
            var bi = FindWordBreak(remaining, width);
            result.Add(remaining[..bi].TrimEnd());
            remaining = remaining[bi..].TrimStart();
        }
        if (remaining.Length > 0)
            result.Add(remaining);
        return result;
    }

    /// <summary>
    /// Finds the best word-break position at or before maxLen.
    /// Falls back to maxLen if no space is found.
    /// </summary>
    private static int FindWordBreak(string text, int maxLen)
    {
        if (maxLen >= text.Length) return text.Length;
        // Look backwards for a space
        var pos = text.LastIndexOf(' ', maxLen - 1);
        return pos > 0 ? pos + 1 : maxLen; // break after the space
    }

    private static string TimeOfDayGreeting(DateTimeOffset now) => now.Hour switch
    {
        < 12 => "Good morning!",
        < 17 => "Good afternoon!",
        _ => "Good evening!"
    };

    /// <summary>
    /// Returns a short timezone abbreviation. On Linux, Label is already short (e.g. "EST").
    /// On Windows, Label can be long (e.g. "Eastern Standard Time") — extract initials.
    /// </summary>
    private static string Abbreviate(string label)
    {
        // Already short (5 chars or fewer) — use as-is
        if (label.Length <= 5) return label;

        // Extract uppercase initials from multi-word name
        var initials = string.Concat(label.Split(' ')
            .Where(w => w.Length > 0)
            .Select(w => w[0]));
        return initials.Length >= 2 ? initials : label;
    }

    private static string Ordinal(int n)
    {
        var suffix = (n % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (n % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            }
        };
        return $"{n}{suffix}";
    }
}

internal enum TihLineKind { Greeting, GreetingCont, ClockLine, SectionHeader, Entry, EntryCont, Blank }

internal sealed record TihLine(TihLineKind Kind, string Text, string? Year);
