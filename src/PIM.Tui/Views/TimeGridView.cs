using PIM.Core.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace PIM.Tui.Views;

/// <summary>
/// Custom-drawn 4-day time grid with 15-minute slots.
/// Shows events as colored blocks spanning their duration.
/// </summary>
internal sealed class TimeGridView : View
{
    private const int SlotsPerHour = 4;
    private const int TotalSlots = 24 * SlotsPerHour; // 96
    private const int HeaderRows = 2;
    private const int TimeGutterWidth = 6;
    private const int SeparatorCount = 4; // 1 after gutter + 3 between columns
    private const int DefaultStartSlot = 8 * SlotsPerHour; // 08:00

    private readonly TuiApp _app;
    private readonly Action<int> _onWindowShift;
    private readonly Action<CalendarEvent> _onEditEvent;
    private readonly Action<DateTimeOffset> _onNewEvent;

    private int _scrollOffset = DefaultStartSlot;
    private int _cursorSlot = DefaultStartSlot;
    private int _cursorDay; // 0-3

    private DateTimeOffset _windowStart;
    private readonly CalendarEvent?[,] _grid = new CalendarEvent?[4, TotalSlots];

    public TimeGridView(
        TuiApp app,
        Action<int> onWindowShift,
        Action<CalendarEvent> onEditEvent,
        Action<DateTimeOffset> onNewEvent)
    {
        _app = app;
        _onWindowShift = onWindowShift;
        _onEditEvent = onEditEvent;
        _onNewEvent = onNewEvent;
        CanFocus = true;
        X = 0; Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        _windowStart = DateTimeOffset.Now.Date;

        KeyDown += HandleKeyDown;
    }

    internal void SetEvents(DateTimeOffset windowStart, List<CalendarEvent> events)
    {
        _windowStart = windowStart;
        RebuildGrid(events);
        SetNeedsDraw();
    }

    internal CalendarEvent? GetEventAtCursor() => _grid[_cursorDay, _cursorSlot];

    internal DateTimeOffset GetCursorDateTime()
    {
        var day = _windowStart.AddDays(_cursorDay);
        return day.AddMinutes(_cursorSlot * 15);
    }

    private void RebuildGrid(List<CalendarEvent> events)
    {
        Array.Clear(_grid);

        foreach (var evt in events)
        {
            for (var day = 0; day < 4; day++)
            {
                var dayStart = _windowStart.AddDays(day);
                var dayEnd = dayStart.AddDays(1);

                if (evt.Start >= dayEnd || evt.End <= dayStart)
                    continue;

                var localStart = evt.Start.ToLocalTime();
                var localEnd = evt.End.ToLocalTime();

                int startSlot;
                if (localStart.Date < dayStart.LocalDateTime.Date)
                    startSlot = 0;
                else
                    startSlot = localStart.Hour * SlotsPerHour + localStart.Minute / 15;

                int endSlot;
                if (localEnd.Date > dayStart.LocalDateTime.Date)
                    endSlot = TotalSlots;
                else
                    endSlot = localEnd.Hour * SlotsPerHour + (localEnd.Minute + 14) / 15;

                startSlot = Math.Clamp(startSlot, 0, TotalSlots - 1);
                endSlot = Math.Clamp(endSlot, startSlot + 1, TotalSlots);

                for (var slot = startSlot; slot < endSlot; slot++)
                    _grid[day, slot] ??= evt;
            }
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var height = Viewport.Height;
        if (width <= TimeGutterWidth + SeparatorCount || height <= HeaderRows)
            return true;

        var colWidth = (width - TimeGutterWidth - SeparatorCount) / 4;
        if (colWidth < 3) colWidth = 3;

        var eventAttr = new GuiAttribute(StandardColor.White, StandardColor.Blue);
        var cursorEventAttr = new GuiAttribute(StandardColor.Yellow, StandardColor.Blue);

        // Row 0: day headers
        Move(0, 0);
        SetAttributeForRole(VisualRole.HotNormal);
        AddStr(new string(' ', TimeGutterWidth));
        AddRune('|');
        for (var day = 0; day < 4; day++)
        {
            var dayDate = _windowStart.AddDays(day);
            AddStr(PadCenter($"{dayDate:ddd d}", colWidth));
            if (day < 3) AddRune('|');
        }

        // Row 1: separator line
        Move(0, 1);
        SetAttributeForRole(VisualRole.Normal);
        AddStr(new string('-', TimeGutterWidth));
        AddRune('+');
        for (var day = 0; day < 4; day++)
        {
            AddStr(new string('-', colWidth));
            if (day < 3) AddRune('+');
        }

        // Rows 2+: time slots
        var visibleSlots = height - HeaderRows;
        for (var row = 0; row < visibleSlots; row++)
        {
            var slot = _scrollOffset + row;
            if (slot >= TotalSlots) break;

            var screenRow = row + HeaderRows;
            var hour = slot / SlotsPerHour;
            var minute = (slot % SlotsPerHour) * 15;

            // Time gutter
            Move(0, screenRow);
            SetAttributeForRole(VisualRole.Normal);
            var timeLabel = minute switch
            {
                0 => $"{hour:D2}:{minute:D2} ",
                30 => "  :30 ",
                _ => "      "
            };
            AddStr(timeLabel);
            AddRune('|');

            // Day columns
            for (var day = 0; day < 4; day++)
            {
                var evt = _grid[day, slot];
                var isCursor = slot == _cursorSlot && day == _cursorDay;

                if (evt is not null)
                {
                    SetAttribute(isCursor ? cursorEventAttr : eventAttr);
                    var isFirstSlot = slot == 0 || _grid[day, slot - 1] != evt;
                    var text = isFirstSlot ? evt.Summary : "";
                    AddStr(Fit(text, colWidth));
                }
                else if (isCursor)
                {
                    SetAttributeForRole(VisualRole.Focus);
                    AddStr(new string(' ', colWidth));
                }
                else
                {
                    SetAttributeForRole(VisualRole.Normal);
                    AddStr(new string(' ', colWidth));
                }

                if (day < 3)
                {
                    SetAttributeForRole(VisualRole.Normal);
                    AddRune('|');
                }
            }
        }

        return true;
    }

    private void HandleKeyDown(object? sender, Key e)
    {
        if (e == Key.CursorUp)
        {
            if (_cursorSlot > 0)
            {
                _cursorSlot--;
                EnsureCursorVisible();
                SetNeedsDraw();
            }
            e.Handled = true;
        }
        else if (e == Key.CursorDown)
        {
            if (_cursorSlot < TotalSlots - 1)
            {
                _cursorSlot++;
                EnsureCursorVisible();
                SetNeedsDraw();
            }
            e.Handled = true;
        }
        else if (e == Key.CursorLeft)
        {
            if (_cursorDay > 0)
            {
                _cursorDay--;
                SetNeedsDraw();
            }
            else
            {
                _onWindowShift(-1);
            }
            e.Handled = true;
        }
        else if (e == Key.CursorRight)
        {
            if (_cursorDay < 3)
            {
                _cursorDay++;
                SetNeedsDraw();
            }
            else
            {
                _onWindowShift(1);
            }
            e.Handled = true;
        }
        else if (e == Key.PageUp)
        {
            var jump = Math.Max(1, Viewport.Height - HeaderRows);
            _cursorSlot = Math.Max(0, _cursorSlot - jump);
            EnsureCursorVisible();
            SetNeedsDraw();
            e.Handled = true;
        }
        else if (e == Key.PageDown)
        {
            var jump = Math.Max(1, Viewport.Height - HeaderRows);
            _cursorSlot = Math.Min(TotalSlots - 1, _cursorSlot + jump);
            EnsureCursorVisible();
            SetNeedsDraw();
            e.Handled = true;
        }
        else if (e == Key.T)
        {
            JumpToCurrentTime();
            e.Handled = true;
        }
        else if (e == Key.Enter)
        {
            var evt = GetEventAtCursor();
            if (evt is not null)
                _onEditEvent(evt);
            e.Handled = true;
        }
        else if (e == Key.N)
        {
            _onNewEvent(GetCursorDateTime());
            e.Handled = true;
        }
        else if (e == Key.Q)
        {
            _app.App?.RequestStop();
            e.Handled = true;
        }
    }

    private void JumpToCurrentTime()
    {
        var now = DateTimeOffset.Now;
        _cursorSlot = Math.Clamp(now.Hour * SlotsPerHour + now.Minute / 15, 0, TotalSlots - 1);
        EnsureCursorVisible();
        SetNeedsDraw();
    }

    private void EnsureCursorVisible()
    {
        var visibleSlots = Math.Max(1, Viewport.Height - HeaderRows);
        if (_cursorSlot < _scrollOffset)
            _scrollOffset = _cursorSlot;
        else if (_cursorSlot >= _scrollOffset + visibleSlots)
            _scrollOffset = _cursorSlot - visibleSlots + 1;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, TotalSlots - visibleSlots));
    }

    private static string PadCenter(string text, int width)
    {
        if (text.Length >= width) return text[..width];
        var left = (width - text.Length) / 2;
        return text.PadLeft(left + text.Length).PadRight(width);
    }

    private static string Fit(string text, int width)
    {
        if (text.Length <= width) return text.PadRight(width);
        return text[..width];
    }
}
