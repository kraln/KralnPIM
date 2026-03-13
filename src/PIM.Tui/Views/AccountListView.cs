using PIM.Tui.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace PIM.Tui.Views;

/// <summary>
/// Compact custom-drawn account list with per-account foreground colors.
/// Accounts can be toggled on/off to filter the email list.
/// </summary>
internal sealed class AccountListView : View
{
    private readonly TuiApp _app;
    private List<AccountOverview> _accounts = [];
    private readonly Dictionary<string, Color> _accountColors = new();
    private readonly HashSet<string> _disabledAccounts = new();
    private int _selectedIndex;

    public event Action? FilterChanged;
    public event Action<string>? ReauthRequested;

    public AccountListView(TuiApp app)
    {
        _app = app;
        CanFocus = true;
        KeyDown += HandleKeyDown;
        MouseEvent += HandleMouseEvent;
    }

    public HashSet<string> DisabledAccountIds => _disabledAccounts;

    public void SetAccounts(List<AccountOverview> accounts)
    {
        _accounts = accounts;
        if (_selectedIndex >= _accounts.Count)
            _selectedIndex = Math.Max(0, _accounts.Count - 1);
        EnsureColors();
        SetNeedsDraw();
    }

    private void EnsureColors()
    {
        foreach (var a in _accounts)
        {
            if (_accountColors.ContainsKey(a.Id)) continue;
            _accountColors[a.Id] = _app.GetOrAssignAccountColor(a.Id);
        }
    }

    private void ToggleSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _accounts.Count) return;
        var id = _accounts[_selectedIndex].Id;
        if (!_disabledAccounts.Remove(id))
            _disabledAccounts.Add(id);
        SetNeedsDraw();
        FilterChanged?.Invoke();
    }

    private void HandleKeyDown(object? sender, Key e)
    {
        if (e == Key.CursorUp && _selectedIndex > 0)
        {
            _selectedIndex--;
            SetNeedsDraw();
            e.Handled = true;
        }
        else if (e == Key.CursorDown && _selectedIndex < _accounts.Count - 1)
        {
            _selectedIndex++;
            SetNeedsDraw();
            e.Handled = true;
        }
        else if (e == Key.Space || e == Key.Enter)
        {
            ToggleSelected();
            e.Handled = true;
        }
        else if (e == Key.R)
        {
            TryRequestReauth();
            e.Handled = true;
        }
    }

    private void HandleMouseEvent(object? sender, Mouse e)
    {
        if (e.Flags.HasFlag(MouseFlags.LeftButtonPressed) && e.Position is { } pos)
        {
            if (pos.Y >= 0 && pos.Y < _accounts.Count)
            {
                _selectedIndex = pos.Y;
                ToggleSelected();
            }
            if (!HasFocus) SetFocus();
            e.Handled = true;
        }
    }

    private void TryRequestReauth()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _accounts.Count) return;
        var id = _accounts[_selectedIndex].Id;
        if (_app.GetAccountOfflineReason(id) == "auth_required")
            ReauthRequested?.Invoke(id);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var height = Viewport.Height;

        SetAttribute(Sol.Normal);
        for (var r = 0; r < height; r++)
        {
            Move(0, r);
            AddStr(new string(' ', width));
        }

        for (var i = 0; i < _accounts.Count && i < height; i++)
        {
            var a = _accounts[i];
            var fg = _accountColors.GetValueOrDefault(a.Id, Sol.Base0);
            var disabled = _disabledAccounts.Contains(a.Id);
            var selected = i == _selectedIndex && HasFocus;
            var bg = selected ? Sol.Base02 : Sol.Base03;

            Move(0, i);

            // Indicator: ● enabled, ○ disabled, in account color (dimmed if disabled)
            var indicatorFg = disabled ? Sol.Base01 : fg;
            SetAttribute(new GuiAttribute(indicatorFg, bg));
            AddStr(disabled ? "\u25cb " : "\u25cf ");

            // Account details — dimmed if disabled
            var textFg = disabled ? Sol.Base01 : fg;
            SetAttribute(new GuiAttribute(textFg, bg));

            var offlineTag = _app.GetAccountOfflineReason(a.Id) == "auth_required" ? " [RE-AUTH]" : " [OFFLINE]";
            var status = _app.IsAccountOnline(a.Id) ? "" : offlineTag;
            var text = $"{a.DisplayName} ({a.Type}){status}  U:{a.UnreadCount} F:{a.FlaggedCount}";
            if (text.Length > width - 2)
                text = text[..(width - 2)];
            AddStr(text);

            // Pad rest of line with bg color for selected highlight
            var remaining = width - 2 - text.Length;
            if (remaining > 0) AddStr(new string(' ', remaining));
        }

        return true;
    }
}
