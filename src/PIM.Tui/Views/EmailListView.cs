using PIM.Core.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace PIM.Tui.Views;

/// <summary>
/// Custom two-line-per-email list view with account-colored unread indicators,
/// right-aligned dates, proper truncation, and email-granularity scrolling.
/// </summary>
internal sealed class EmailListView : View
{
    private readonly TuiApp _app;
    private List<EmailHeader> _emails = [];
    private int _selectedIndex = -1;
    private int _scrollOffset;

    // Cached per-account indicator attributes (normal + selected variants)
    private readonly Dictionary<string, (GuiAttribute normal, GuiAttribute selected)> _accountAttrs = new();

    public event Action<EmailHeader?>? SelectionChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            value = Math.Clamp(value, _emails.Count > 0 ? 0 : -1, _emails.Count - 1);
            if (value == _selectedIndex) return;
            _selectedIndex = value;
            EnsureVisible();
            SetNeedsDraw();
        }
    }

    public EmailHeader? SelectedEmail =>
        _selectedIndex >= 0 && _selectedIndex < _emails.Count
            ? _emails[_selectedIndex]
            : null;

    public EmailListView(TuiApp app)
    {
        _app = app;
        CanFocus = true;

        // Use KeyDown event (not OnKeyDown override) — matches TimeGridView pattern
        KeyDown += HandleKeyDown;

        // Mouse: click to select, scroll wheel to navigate
        MouseEvent += HandleMouseEvent;
    }

    public void SetEmails(List<EmailHeader> emails)
    {
        _emails = emails;
        EnsureAccountColors();

        var oldIndex = _selectedIndex;
        if (_selectedIndex >= _emails.Count)
            _selectedIndex = _emails.Count - 1;
        if (_selectedIndex < 0 && _emails.Count > 0)
            _selectedIndex = 0;

        EnsureVisible();
        SetNeedsDraw();

        // Fire selection changed if selection moved (e.g., initial load)
        if (_selectedIndex != oldIndex && _selectedIndex >= 0)
            SelectionChanged?.Invoke(SelectedEmail);
    }

    public void UpdateEmail(int index, EmailHeader header)
    {
        if (index >= 0 && index < _emails.Count)
        {
            _emails[index] = header;
            SetNeedsDraw();
        }
    }

    public void RemoveEmail(int index)
    {
        if (index < 0 || index >= _emails.Count) return;
        _emails.RemoveAt(index);
        if (_selectedIndex >= _emails.Count)
            _selectedIndex = _emails.Count - 1;
        EnsureVisible();
        SetNeedsDraw();
    }

    private int VisibleCount => Math.Max(1, Viewport.Height / 2);

    private void EnsureVisible()
    {
        if (_selectedIndex < 0) return;
        if (_selectedIndex < _scrollOffset)
            _scrollOffset = _selectedIndex;
        var vc = VisibleCount;
        if (_selectedIndex >= _scrollOffset + vc)
            _scrollOffset = _selectedIndex - vc + 1;
        if (_scrollOffset < 0) _scrollOffset = 0;
    }

    private void MoveTo(int delta)
    {
        var next = Math.Clamp(_selectedIndex + delta, 0, _emails.Count - 1);
        if (next == _selectedIndex) return;
        _selectedIndex = next;
        EnsureVisible();
        SetNeedsDraw();
        SelectionChanged?.Invoke(SelectedEmail);
    }

    private void HandleKeyDown(object? sender, Key e)
    {
        if (e == Key.CursorDown)
        {
            MoveTo(1);
            e.Handled = true;
        }
        else if (e == Key.CursorUp)
        {
            MoveTo(-1);
            e.Handled = true;
        }
        else if (e == Key.Home)
        {
            MoveTo(-_emails.Count);
            e.Handled = true;
        }
        else if (e == Key.End)
        {
            MoveTo(_emails.Count);
            e.Handled = true;
        }
    }

    private void HandleMouseEvent(object? sender, Mouse e)
    {
        if (e.Flags.HasFlag(MouseFlags.LeftButtonPressed) && e.Position is { } pos)
        {
            // Two lines per email — map Y position to email index
            var emailIdx = _scrollOffset + pos.Y / 2;
            if (emailIdx >= 0 && emailIdx < _emails.Count && emailIdx != _selectedIndex)
            {
                _selectedIndex = emailIdx;
                EnsureVisible();
                SetNeedsDraw();
                SelectionChanged?.Invoke(SelectedEmail);
            }
            if (!HasFocus) SetFocus();
            e.Handled = true;
        }
        else if (e.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            MoveTo(3);
            e.Handled = true;
        }
        else if (e.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            MoveTo(-3);
            e.Handled = true;
        }
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

        if (_emails.Count == 0 || width < 5) return true;

        var visibleCount = height / 2;
        for (var vi = 0; vi < visibleCount; vi++)
        {
            var emailIdx = _scrollOffset + vi;
            if (emailIdx >= _emails.Count) break;

            var email = _emails[emailIdx];
            var selected = emailIdx == _selectedIndex;
            var y = vi * 2;

            DrawLine1(email, selected, y, width);
            DrawLine2(email, selected, y + 1, width);
        }

        return true;
    }

    private void DrawLine1(EmailHeader m, bool selected, int y, int width)
    {
        var bg = selected ? Sol.Base02 : Sol.Base03;

        // Unread indicator with account color foreground
        var unread = m.IsRead ? "\u25cb" : "\u25cf";
        var (normalIndicator, selectedIndicator) = GetAccountAttr(m.AccountId);
        Move(0, y);
        SetAttribute(selected ? selectedIndicator : normalIndicator);
        AddStr(unread);

        // Sender
        var senderName = StripZeroWidth(m.FromDisplayName ?? "");
        var senderEmail = StripZeroWidth(m.FromAddress ?? "");
        var sender = string.IsNullOrEmpty(senderName) ? senderEmail : $"{senderName} <{senderEmail}>";

        // Date (right-aligned)
        var date = m.Date.ToLocalTime().ToString("MMM d HH:mm");
        var dateLen = date.Length;

        // Layout: [indicator(1)] [space(1)] [sender...] [space(1)] [date]
        var senderSpace = width - 2 - dateLen - 1;
        if (senderSpace < 0) senderSpace = 0;

        var displaySender = Truncate(sender, senderSpace);

        var senderAttr = new GuiAttribute(
            selected ? Sol.Base3 : (m.IsRead ? Sol.Base0 : Sol.Base1),
            bg);
        SetAttribute(senderAttr);
        AddStr(" ");
        AddStr(displaySender);

        // Pad to right-align date
        var pad = senderSpace - displaySender.Length;
        if (pad > 0) AddStr(new string(' ', pad));

        var dateAttr = new GuiAttribute(selected ? Sol.Base1 : Sol.Base01, bg);
        SetAttribute(dateAttr);
        AddStr(" ");
        AddStr(date);
    }

    private void DrawLine2(EmailHeader m, bool selected, int y, int width)
    {
        var bg = selected ? Sol.Base02 : Sol.Base03;

        Move(0, y);

        // Gutter col 0: flag icon (red), col 1: attachment icon
        if (m.IsFlagged)
        {
            SetAttribute(new GuiAttribute(Sol.Red, bg));
            AddStr("\u2691");
        }
        else
        {
            SetAttribute(new GuiAttribute(selected ? Sol.Base1 : Sol.Base01, bg));
            AddStr(" ");
        }

        var attachAttr = new GuiAttribute(selected ? Sol.Base1 : Sol.Base01, bg);
        SetAttribute(attachAttr);
        AddStr(m.Attachments.Count > 0 ? "\u235e" : " ");

        // Subject at column 2, aligned with sender on line 1
        var subjectAttr = new GuiAttribute(
            selected ? Sol.Base1 : (m.IsRead ? Sol.Base01 : Sol.Base0),
            bg);
        SetAttribute(subjectAttr);

        var subject = StripZeroWidth(m.Subject ?? "(no subject)");
        var subjectSpace = width - 2;
        if (subjectSpace < 0) subjectSpace = 0;
        var displaySubject = Truncate(subject, subjectSpace);
        AddStr(displaySubject);

        // Clear rest of line
        var remaining = subjectSpace - displaySubject.Length;
        if (remaining > 0) AddStr(new string(' ', remaining));
    }

    private void EnsureAccountColors()
    {
        foreach (var email in _emails)
        {
            if (_accountAttrs.ContainsKey(email.AccountId)) continue;

            var fg = _app.GetOrAssignAccountColor(email.AccountId);

            _accountAttrs[email.AccountId] = (
                normal: new GuiAttribute(fg, Sol.Base03),
                selected: new GuiAttribute(fg, Sol.Base02)
            );
        }
    }

    private (GuiAttribute normal, GuiAttribute selected) GetAccountAttr(string accountId)
    {
        if (_accountAttrs.TryGetValue(accountId, out var pair))
            return pair;

        // Fallback (shouldn't normally happen after EnsureAccountColors)
        var fg = _app.GetOrAssignAccountColor(accountId);
        var result = (new GuiAttribute(fg, Sol.Base03), new GuiAttribute(fg, Sol.Base02));
        _accountAttrs[accountId] = result;
        return result;
    }

    private static string Truncate(string text, int maxWidth)
    {
        if (maxWidth <= 0) return "";
        if (text.Length <= maxWidth) return text;
        if (maxWidth == 1) return "\u2026";
        return text[..(maxWidth - 1)] + "\u2026";
    }

    private static string StripZeroWidth(string value) =>
        string.Concat(value.Where(c => c is not ('\u200B' or '\u200C' or '\u200D' or '\uFEFF')));
}
