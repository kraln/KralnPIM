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
    private const int BaseHeaderRows = 2; // row 0: day names, row 1: forecast
    private const int TimeGutterWidth = 6;
    private const int SeparatorCount = 4; // 1 after gutter + 3 between columns
    private const int DefaultStartSlot = 8 * SlotsPerHour; // 08:00

    private readonly TuiApp _app;
    private readonly Action<int> _onWindowShift;
    private readonly Action<CalendarEvent> _onEditEvent;
    private readonly Action<DateTimeOffset> _onNewEvent;
    private readonly Action _onJumpToToday;

    private int _scrollOffset = DefaultStartSlot;
    private int _cursorSlot = DefaultStartSlot;
    private int _cursorDay; // 0-3

    private DateTimeOffset _windowStart;
    private readonly CalendarEvent?[,] _grid = new CalendarEvent?[4, TotalSlots];

    // Per-day all-day event summaries for the banner row
    private readonly string?[] _allDayLabels = new string?[4];
    private bool _hasAllDay;
    private int HeaderRows => _hasAllDay ? BaseHeaderRows + 1 : BaseHeaderRows;

    // Per-day sun times: _sunSlots[day] = (sunriseSlot, sunsetSlot), -1 if unavailable
    private readonly (int sunrise, int sunset)[] _sunSlots = [(-1, -1), (-1, -1), (-1, -1), (-1, -1)];
    // Per-day forecast summary for header row (e.g. "Rain 18/12")
    private readonly string?[] _forecastLabels = new string?[4];

    public TimeGridView(
        TuiApp app,
        Action<int> onWindowShift,
        Action<CalendarEvent> onEditEvent,
        Action<DateTimeOffset> onNewEvent,
        Action onJumpToToday)
    {
        _app = app;
        _onWindowShift = onWindowShift;
        _onEditEvent = onEditEvent;
        _onNewEvent = onNewEvent;
        _onJumpToToday = onJumpToToday;
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

    internal void SetDailyForecasts(List<DailyForecast> forecasts)
    {
        for (var day = 0; day < 4; day++)
        {
            var date = DateOnly.FromDateTime(_windowStart.AddDays(day).LocalDateTime.Date);
            var fc = forecasts.FirstOrDefault(f => f.Date == date);
            if (fc is not null)
            {
                var sr = fc.Sunrise.HasValue ? fc.Sunrise.Value.Hour * SlotsPerHour + fc.Sunrise.Value.Minute / 15 : -1;
                var ss = fc.Sunset.HasValue ? fc.Sunset.Value.Hour * SlotsPerHour + fc.Sunset.Value.Minute / 15 : -1;
                _sunSlots[day] = (sr, ss);

                var parts = new List<string>();
                if (fc.Condition is not null) parts.Add(fc.Condition);
                if (fc.HighCelsius.HasValue && fc.LowCelsius.HasValue)
                    parts.Add($"{fc.HighCelsius.Value:F0}\u00b0/{fc.LowCelsius.Value:F0}\u00b0");
                _forecastLabels[day] = parts.Count > 0 ? string.Join(" ", parts) : null;
            }
            else
            {
                _sunSlots[day] = (-1, -1);
                _forecastLabels[day] = null;
            }
        }
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
        Array.Clear(_allDayLabels);
        _hasAllDay = false;

        // Collect all-day labels per day
        var allDayPerDay = new List<string>[4];
        for (var i = 0; i < 4; i++) allDayPerDay[i] = [];

        foreach (var evt in events)
        {
            if (evt.IsAllDay)
            {
                for (var day = 0; day < 4; day++)
                {
                    var dayStart = _windowStart.AddDays(day);
                    var dayEnd = dayStart.AddDays(1);
                    if (evt.Start < dayEnd && evt.End > dayStart)
                        allDayPerDay[day].Add(evt.Summary);
                }
                continue;
            }

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

        for (var day = 0; day < 4; day++)
        {
            if (allDayPerDay[day].Count > 0)
            {
                _allDayLabels[day] = string.Join(", ", allDayPerDay[day]);
                _hasAllDay = true;
            }
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        // Clear stale content
        SetAttributeForRole(VisualRole.Normal);
        for (var r = 0; r < Viewport.Height; r++)
        {
            Move(0, r);
            AddStr(new string(' ', Viewport.Width));
        }

        var width = Viewport.Width;
        var height = Viewport.Height;
        if (width <= TimeGutterWidth + SeparatorCount || height <= HeaderRows)
            return true;

        var colWidth = (width - TimeGutterWidth - SeparatorCount) / 4;
        if (colWidth < 3) colWidth = 3;

        var eventAttr = new GuiAttribute(StandardColor.White, StandardColor.Blue);
        var cursorEventAttr = new GuiAttribute(StandardColor.Yellow, StandardColor.Blue);
        var nowLineAttr = new GuiAttribute(StandardColor.BrightRed, StandardColor.Black);
        var sunLineAttr = new GuiAttribute(StandardColor.Yellow, StandardColor.Black);
        var forecastAttr = new GuiAttribute(StandardColor.Cyan, StandardColor.Black);

        // Pre-compute 'now' marker
        var now = DateTimeOffset.Now;
        var nowSlot = now.Hour * SlotsPerHour + now.Minute / 15;
        var todayDate = now.Date;

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

        // Row 1: forecast labels
        Move(0, 1);
        SetAttribute(forecastAttr);
        AddStr(new string(' ', TimeGutterWidth));
        AddRune('|');
        for (var day = 0; day < 4; day++)
        {
            var label = _forecastLabels[day] ?? "";
            AddStr(PadCenter(label, colWidth));
            if (day < 3) AddRune('|');
        }

        // Row 2 (conditional): all-day banner
        if (_hasAllDay)
        {
            var allDayAttr = new GuiAttribute(StandardColor.White, StandardColor.DarkGray);
            Move(0, BaseHeaderRows);
            SetAttribute(allDayAttr);
            AddStr(PadCenter("", TimeGutterWidth));
            AddRune('|');
            for (var day = 0; day < 4; day++)
            {
                var label = _allDayLabels[day] ?? "";
                AddStr(Fit(label, colWidth));
                if (day < 3) AddRune('|');
            }
        }

        // Rows 2+/3+: time slots
        var visibleSlots = height - HeaderRows;
        for (var row = 0; row < visibleSlots; row++)
        {
            var slot = _scrollOffset + row;
            if (slot >= TotalSlots) break;

            var screenRow = row + HeaderRows;
            var hour = slot / SlotsPerHour;
            var minute = (slot % SlotsPerHour) * 15;

            // Check if 'now' line applies to any visible day column
            var isNowLine = false;
            for (var day = 0; day < 4; day++)
            {
                if (_windowStart.AddDays(day).LocalDateTime.Date == todayDate && slot == nowSlot)
                    isNowLine = true;
            }

            // Time gutter label
            Move(0, screenRow);
            string timeLabel;
            if (isNowLine && minute != 0 && minute != 30)
            {
                SetAttribute(nowLineAttr);
                timeLabel = " now  ";
            }
            else if (minute == 0)
            {
                SetAttributeForRole(VisualRole.Normal);
                timeLabel = $"{hour:D2}:{minute:D2} ";
            }
            else if (minute == 30)
            {
                SetAttributeForRole(VisualRole.Normal);
                timeLabel = "  :30 ";
            }
            else
            {
                SetAttributeForRole(VisualRole.Normal);
                timeLabel = "      ";
            }
            AddStr(timeLabel);

            // Gutter separator
            SetAttributeForRole(VisualRole.Normal);
            AddRune('|');

            // Day columns
            for (var day = 0; day < 4; day++)
            {
                var evt = _grid[day, slot];
                var isCursor = slot == _cursorSlot && day == _cursorDay;
                var dayIsToday = _windowStart.AddDays(day).LocalDateTime.Date == todayDate;
                var isSunrise = _sunSlots[day].sunrise == slot;
                var isSunset = _sunSlots[day].sunset == slot;

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
                else if (isNowLine && dayIsToday)
                {
                    SetAttribute(nowLineAttr);
                    AddStr(new string('-', colWidth));
                }
                else if (isSunrise || isSunset)
                {
                    SetAttribute(sunLineAttr);
                    var label = isSunrise ? "\U0001f305" : "\U0001f307";
                    AddStr(Fit(label, colWidth));
                }
                else
                {
                    SetAttributeForRole(VisualRole.Normal);
                    AddStr(new string(' ', colWidth));
                }

                // Column separator
                if (day < 3)
                {
                    if (isNowLine && dayIsToday)
                    {
                        SetAttribute(nowLineAttr);
                        AddRune('-');
                    }
                    else
                    {
                        SetAttributeForRole(VisualRole.Normal);
                        AddRune('|');
                    }
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
        // Let '?' bubble up to TuiApp's global handler
    }

    internal void JumpToCurrentTime()
    {
        var now = DateTimeOffset.Now;
        _cursorSlot = Math.Clamp(now.Hour * SlotsPerHour + now.Minute / 15, 0, TotalSlots - 1);
        _cursorDay = 0;
        CenterCursorInViewport();
        SetNeedsDraw();
        _onJumpToToday();
    }

    private void CenterCursorInViewport()
    {
        var visibleSlots = Math.Max(1, Viewport.Height - HeaderRows);
        _scrollOffset = _cursorSlot - visibleSlots / 2;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, TotalSlots - visibleSlots));
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
