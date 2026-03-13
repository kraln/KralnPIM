using PIM.Core.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace PIM.Tui.Views;

/// <summary>
/// Custom-drawn agenda list with yellow day headers and account-colored event indicators.
/// Supports keyboard scrolling, mouse wheel, and optional item selection.
/// </summary>
internal sealed class AgendaListView : View
{
    private readonly TuiApp _app;
    private List<AgendaRow> _rows = [];
    private int _scrollOffset;
    private int _selectedIndex = -1;

    public event Action<CalendarEvent>? EventSelected;

    public AgendaListView(TuiApp app)
    {
        _app = app;
        CanFocus = true;
        KeyDown += HandleKeyDown;
        MouseEvent += HandleMouseEvent;
    }

    public CalendarEvent? SelectedEvent =>
        _selectedIndex >= 0 && _selectedIndex < _rows.Count
            ? _rows[_selectedIndex].CalEvent
            : null;

    public void SetRows(List<AgendaRow> rows)
    {
        _rows = rows;
        // Reset selection to first event row
        _selectedIndex = _rows.FindIndex(r => r.Kind == AgendaRowKind.Event);
        if (_scrollOffset > Math.Max(0, _rows.Count - Viewport.Height))
            _scrollOffset = Math.Max(0, _rows.Count - Viewport.Height);
        EnsureVisible();
        SetNeedsDraw();
    }

    private void EnsureVisible()
    {
        if (_selectedIndex < 0) return;
        var height = Math.Max(1, Viewport.Height);
        if (_selectedIndex < _scrollOffset)
            _scrollOffset = _selectedIndex;
        if (_selectedIndex >= _scrollOffset + height)
            _scrollOffset = _selectedIndex - height + 1;
        if (_scrollOffset < 0) _scrollOffset = 0;
    }

    private void MoveSelection(int delta)
    {
        if (_rows.Count == 0) return;
        var next = _selectedIndex;
        // Skip non-event rows in the direction of movement
        for (var i = 0; i < _rows.Count; i++)
        {
            next += delta > 0 ? 1 : -1;
            if (next < 0 || next >= _rows.Count) return;
            if (_rows[next].Kind == AgendaRowKind.Event) break;
        }
        if (next < 0 || next >= _rows.Count || _rows[next].Kind != AgendaRowKind.Event) return;
        _selectedIndex = next;
        EnsureVisible();
        SetNeedsDraw();
    }

    private void HandleKeyDown(object? sender, Key e)
    {
        var pageSize = Math.Max(1, Viewport.Height);
        if (e == Key.CursorDown || e == Key.J)
        {
            MoveSelection(1);
            e.Handled = true;
        }
        else if (e == Key.CursorUp || e == Key.K)
        {
            MoveSelection(-1);
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
        else if (e == Key.Enter)
        {
            if (SelectedEvent is { } evt)
            {
                EventSelected?.Invoke(evt);
                e.Handled = true;
            }
        }
    }

    private void HandleMouseEvent(object? sender, Mouse e)
    {
        if (e.Flags.HasFlag(MouseFlags.LeftButtonPressed) && e.Position is { } pos)
        {
            var rowIdx = _scrollOffset + pos.Y;
            if (rowIdx >= 0 && rowIdx < _rows.Count && _rows[rowIdx].Kind == AgendaRowKind.Event)
            {
                _selectedIndex = rowIdx;
                EnsureVisible();
                SetNeedsDraw();
            }
            if (!HasFocus) SetFocus();
            e.Handled = true;
        }
        else if (e.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked) && e.Position is { } dpos)
        {
            var rowIdx = _scrollOffset + dpos.Y;
            if (rowIdx >= 0 && rowIdx < _rows.Count && _rows[rowIdx].CalEvent is { } evt)
            {
                _selectedIndex = rowIdx;
                EventSelected?.Invoke(evt);
            }
            e.Handled = true;
        }
        else if (e.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            Scroll(3);
            e.Handled = true;
        }
        else if (e.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            Scroll(-3);
            e.Handled = true;
        }
    }

    private void Scroll(int delta)
    {
        var maxOffset = Math.Max(0, _rows.Count - Viewport.Height);
        var next = Math.Clamp(_scrollOffset + delta, 0, maxOffset);
        if (next == _scrollOffset) return;
        _scrollOffset = next;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var height = Viewport.Height;

        // Clear
        SetAttribute(Sol.Normal);
        for (var r = 0; r < height; r++)
        {
            Move(0, r);
            AddStr(new string(' ', width));
        }

        for (var vi = 0; vi < height; vi++)
        {
            var rowIdx = _scrollOffset + vi;
            if (rowIdx >= _rows.Count) break;

            var row = _rows[rowIdx];
            var selected = rowIdx == _selectedIndex && HasFocus;
            var bg = selected ? Sol.Base02 : Sol.Base03;
            Move(0, vi);

            switch (row.Kind)
            {
                case AgendaRowKind.DayHeader:
                    SetAttribute(Sol.Heading);
                    var header = row.Text.Length > width ? row.Text[..width] : row.Text;
                    AddStr(header);
                    break;

                case AgendaRowKind.Event:
                {
                    // Account-colored circle
                    var fg = row.AccountId is not null
                        ? _app.GetOrAssignAccountColor(row.AccountId)
                        : Sol.Base0;
                    SetAttribute(new GuiAttribute(fg, bg));
                    AddStr("\u25cf ");

                    // Event text
                    var textFg = selected ? Sol.Base1 : Sol.Base0;
                    SetAttribute(new GuiAttribute(textFg, bg));
                    var maxText = width - 2;
                    if (maxText > 0)
                    {
                        var text = row.Text.Length > maxText ? row.Text[..(maxText - 1)] + "\u2026" : row.Text;
                        AddStr(text);

                        // Pad rest of line for selection highlight
                        var pad = maxText - Math.Min(row.Text.Length, maxText);
                        if (pad > 0) AddStr(new string(' ', pad));
                    }
                    break;
                }

                case AgendaRowKind.Blank:
                    break;
            }
        }

        return true;
    }

}
