using PIM.Core.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace PIM.Tui.Views;

/// <summary>
/// Custom-drawn 4-day time grid with 15-minute slots.
/// Shows events as colored blocks with half-block caps for tight visual boundaries.
/// Events are colored by their account/calendar color using Solarized palette.
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

    private readonly string?[] _allDayLabels = new string?[4];
    private bool _hasAllDay;
    private int HeaderRows => _hasAllDay ? BaseHeaderRows + 1 : BaseHeaderRows;

    private readonly (int sunrise, int sunset)[] _sunSlots = [(-1, -1), (-1, -1), (-1, -1), (-1, -1)];
    private readonly string?[] _forecastLabels = new string?[4];

    // Per-account color attributes and raw bg colors (for half-block caps)
    private readonly Dictionary<string, GuiAttribute> _accountEventAttrs = new();
    private readonly Dictionary<string, GuiAttribute> _accountCursorAttrs = new();
    private readonly Dictionary<string, Color> _accountBgColors = new();
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
        MouseEvent += HandleMouseEvent;
    }

    internal void SetEvents(DateTimeOffset windowStart, List<CalendarEvent> events)
    {
        _windowStart = windowStart;
        EnsureAccountColors(events);
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

    private void EnsureAccountColors(List<CalendarEvent> events)
    {
        foreach (var evt in events)
        {
            if (_accountEventAttrs.ContainsKey(evt.AccountId)) continue;

            var bg = _app.GetOrAssignAccountColor(evt.AccountId);
            _accountEventAttrs[evt.AccountId] = Sol.EventAttr(bg);
            _accountCursorAttrs[evt.AccountId] = Sol.EventCursorAttr(bg);
            _accountBgColors[evt.AccountId] = bg;
        }
    }

    private void RebuildGrid(List<CalendarEvent> events)
    {
        Array.Clear(_grid);
        Array.Clear(_allDayLabels);
        _hasAllDay = false;

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
        SetAttribute(Sol.Normal);
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

        var defaultBg = Sol.Blue;
        var defaultEventAttr = Sol.EventAttr(defaultBg);
        var defaultCursorEventAttr = Sol.EventCursorAttr(defaultBg);

        var now = DateTimeOffset.Now;
        var nowSlot = now.Hour * SlotsPerHour + now.Minute / 15;
        var todayDate = now.Date;

        // Row 0: day headers
        Move(0, 0);
        SetAttribute(Sol.Heading);
        AddStr(new string(' ', TimeGutterWidth));
        SetAttribute(Sol.Dimmed);
        AddRune('|');
        for (var day = 0; day < 4; day++)
        {
            var dayDate = _windowStart.AddDays(day);
            var isToday = dayDate.LocalDateTime.Date == todayDate;
            SetAttribute(isToday ? Sol.Heading : Sol.Emphasis);
            AddStr(PadCenter($"{dayDate:ddd d}", colWidth));
            if (day < 3)
            {
                SetAttribute(Sol.Dimmed);
                AddRune('|');
            }
        }

        // Row 1: forecast labels
        Move(0, 1);
        SetAttribute(Sol.Forecast);
        AddStr(new string(' ', TimeGutterWidth));
        SetAttribute(Sol.Dimmed);
        AddRune('|');
        for (var day = 0; day < 4; day++)
        {
            SetAttribute(Sol.Forecast);
            var label = _forecastLabels[day] ?? "";
            AddStr(PadCenter(label, colWidth));
            if (day < 3)
            {
                SetAttribute(Sol.Dimmed);
                AddRune('|');
            }
        }

        // Row 2 (conditional): all-day banner
        if (_hasAllDay)
        {
            Move(0, BaseHeaderRows);
            SetAttribute(Sol.AllDay);
            AddStr(PadCenter("", TimeGutterWidth));
            AddRune('|');
            for (var day = 0; day < 4; day++)
            {
                var label = _allDayLabels[day] ?? "";
                AddStr(Fit(label, colWidth));
                if (day < 3) AddRune('|');
            }
        }

        // Time slot rows
        var visibleSlots = height - HeaderRows;
        for (var row = 0; row < visibleSlots; row++)
        {
            var slot = _scrollOffset + row;
            if (slot >= TotalSlots) break;

            var screenRow = row + HeaderRows;
            var hour = slot / SlotsPerHour;
            var minute = (slot % SlotsPerHour) * 15;

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
                SetAttribute(Sol.NowLine);
                timeLabel = " now  ";
            }
            else if (minute == 0)
            {
                SetAttribute(Sol.Emphasis);
                timeLabel = $"{hour:D2}:{minute:D2} ";
            }
            else if (minute == 30)
            {
                SetAttribute(Sol.Dimmed);
                timeLabel = "  :30 ";
            }
            else
            {
                SetAttribute(Sol.Normal);
                timeLabel = "      ";
            }
            AddStr(timeLabel);

            SetAttribute(Sol.Dimmed);
            AddRune('|');

            // Day columns
            for (var day = 0; day < 4; day++)
            {
                var evt = _grid[day, slot];
                var isCursor = slot == _cursorSlot && day == _cursorDay;
                var dayIsToday = _windowStart.AddDays(day).LocalDateTime.Date == todayDate;
                var isNowSlot = isNowLine && dayIsToday;
                var isSunrise = _sunSlots[day].sunrise == slot;
                var isSunset = _sunSlots[day].sunset == slot;

                if (evt is not null)
                {
                    var evtBg = _accountBgColors.GetValueOrDefault(evt.AccountId, defaultBg);
                    var evtAttr = _accountEventAttrs.GetValueOrDefault(evt.AccountId, defaultEventAttr);
                    var curAttr = _accountCursorAttrs.GetValueOrDefault(evt.AccountId, defaultCursorEventAttr);

                    var isFirstSlot = slot == 0 || _grid[day, slot - 1] != evt;
                    var isLastSlot = slot == TotalSlots - 1 || _grid[day, slot + 1] != evt;

                    if (isLastSlot && !isFirstSlot)
                    {
                        // Bottom cap: ▀ with event color on top, normal bg on bottom
                        var capFg = isCursor ? Sol.Yellow : evtBg;
                        var capBg = isCursor ? Sol.Base02 : Sol.Base03;
                        SetAttribute(new GuiAttribute(capFg, capBg));
                        AddStr(new string('\u2580', colWidth));
                    }
                    else
                    {
                        // First slot or middle slot or single-slot: full bg + text
                        SetAttribute(isCursor ? curAttr : evtAttr);
                        var text = isFirstSlot ? evt.Summary : "";
                        AddStr(Fit(text, colWidth));
                    }
                }
                else if (isCursor && isNowSlot)
                {
                    // Cursor on the now line: red dashes on highlighted bg
                    SetAttribute(new GuiAttribute(Sol.Red, Sol.Base02));
                    AddStr(new string('\u2500', colWidth));
                }
                else if (isCursor)
                {
                    SetAttribute(Sol.Cursor);
                    AddStr(new string(' ', colWidth));
                }
                else if (isNowSlot)
                {
                    SetAttribute(Sol.NowLine);
                    AddStr(new string('\u2500', colWidth));
                }
                else if (isSunrise || isSunset)
                {
                    SetAttribute(Sol.SunMarker);
                    var label = isSunrise ? "\U0001f305" : "\U0001f307";
                    AddStr(Fit(label, colWidth));
                }
                else
                {
                    SetAttribute(Sol.Normal);
                    AddStr(new string(' ', colWidth));
                }

                if (day < 3)
                {
                    if (isNowSlot)
                    {
                        SetAttribute(isCursor ? new GuiAttribute(Sol.Red, Sol.Base02) : Sol.NowLine);
                        AddRune('\u2500');
                    }
                    else
                    {
                        SetAttribute(Sol.Dimmed);
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
    }

    private void HandleMouseEvent(object? sender, Mouse e)
    {
        if (e.Flags.HasFlag(MouseFlags.LeftButtonPressed) && e.Position is { } pos)
        {
            // Map screen position to (day, slot)
            var screenRow = pos.Y;
            var screenCol = pos.X;

            if (screenRow >= HeaderRows && screenCol > TimeGutterWidth)
            {
                var colOffset = screenCol - TimeGutterWidth - 1; // -1 for gutter separator
                var colWidth = CalculateColWidth();
                var day = colOffset / (colWidth + 1); // +1 for column separators
                var slot = _scrollOffset + (screenRow - HeaderRows);

                if (day >= 0 && day < 4 && slot >= 0 && slot < TotalSlots)
                {
                    _cursorDay = day;
                    _cursorSlot = slot;
                    EnsureCursorVisible();
                    SetNeedsDraw();
                }
            }

            if (!HasFocus) SetFocus();
            e.Handled = true;
        }
        else if (e.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            var jump = SlotsPerHour; // scroll by 1 hour
            _scrollOffset = Math.Min(_scrollOffset + jump,
                Math.Max(0, TotalSlots - (Viewport.Height - HeaderRows)));
            _cursorSlot = Math.Clamp(_cursorSlot + jump, 0, TotalSlots - 1);
            SetNeedsDraw();
            e.Handled = true;
        }
        else if (e.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            var jump = SlotsPerHour;
            _scrollOffset = Math.Max(0, _scrollOffset - jump);
            _cursorSlot = Math.Clamp(_cursorSlot - jump, 0, TotalSlots - 1);
            SetNeedsDraw();
            e.Handled = true;
        }
        else if (e.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked) && e.Position is { } dpos)
        {
            // Double-click: edit event or create new
            var screenRow = dpos.Y;
            var screenCol = dpos.X;

            if (screenRow >= HeaderRows && screenCol > TimeGutterWidth)
            {
                var colOffset = screenCol - TimeGutterWidth - 1;
                var colWidth = CalculateColWidth();
                var day = colOffset / (colWidth + 1);
                var slot = _scrollOffset + (screenRow - HeaderRows);

                if (day >= 0 && day < 4 && slot >= 0 && slot < TotalSlots)
                {
                    _cursorDay = day;
                    _cursorSlot = slot;
                    var evt = GetEventAtCursor();
                    if (evt is not null)
                        _onEditEvent(evt);
                    else
                        _onNewEvent(GetCursorDateTime());
                }
            }
            e.Handled = true;
        }
    }

    private int CalculateColWidth()
    {
        var width = Viewport.Width;
        var colWidth = (width - TimeGutterWidth - SeparatorCount) / 4;
        return Math.Max(3, colWidth);
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
