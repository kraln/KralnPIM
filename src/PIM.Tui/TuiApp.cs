using System.Collections.ObjectModel;
using PIM.Core.Models;
using PIM.Tui.Client;
using PIM.Tui.Models;
using PIM.Tui.Views;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace PIM.Tui;

/// <summary>Disables ListView type-ahead so letter keys reach KeyDown handlers.</summary>
internal sealed class NoTypeAheadMatcher : ICollectionNavigatorMatcher
{
    public bool IsCompatibleKey(Key key) => false;
    public bool IsMatch(string search, object value) => false;
}

internal sealed class TuiApp : Window
{
    private readonly PimApiClient _api;
    private readonly PimWsClient _ws;
    private readonly Label _statusLabel;
    private readonly Label _batteryLabel;
    private readonly DashboardTab _dashboardTab;
    private readonly CalendarTab _calendarTab;
    private readonly EmailTab _emailTab;
    private readonly Dictionary<string, bool> _accountStatus = new();
    private readonly Dictionary<string, string?> _accountColors = new();
    private readonly Dictionary<string, Color> _resolvedAccountColors = new();
    private int _nextPaletteIdx;
    private object? _statusClearTimeout;

    private View _activeView;
    private int _activeIndex; // 0=Dashboard, 1=Calendar, 2=Email

    public TuiApp(PimApiClient api, PimWsClient ws, TihApiClient? tihClient = null)
    {
        _api = api;
        _ws = ws;
        Title = "KralnPIM (q to quit)";

        // Apply Solarized Dark color scheme globally so standard views
        // (Labels, TextViews, FrameViews) inherit dark background colors.
        SchemeManager.AddScheme("Solarized", new Scheme
        {
            Normal = new GuiAttribute(Sol.Base0, Sol.Base03),
            HotNormal = new GuiAttribute(Sol.Yellow, Sol.Base03),
            Focus = new GuiAttribute(Sol.Base1, Sol.Base02),
            HotFocus = new GuiAttribute(Sol.Yellow, Sol.Base02),
            Disabled = new GuiAttribute(Sol.Base01, Sol.Base03),
        });
        SchemeName = "Solarized";

        _dashboardTab = new DashboardTab(api, this, tihClient);
        _calendarTab = new CalendarTab(api, this);
        _emailTab = new EmailTab(api, this);

        _statusLabel = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(20),
            Height = 1,
            Text = "Ready  [1] Dashboard  [2] Calendar  [3] Email"
        };

        _batteryLabel = new Label
        {
            X = Pos.AnchorEnd(20),
            Y = Pos.AnchorEnd(1),
            Width = 20,
            Height = 1,
            Text = ""
        };

        // Position all views to fill the space above the status bar
        foreach (var view in new View[] { _dashboardTab, _calendarTab, _emailTab })
        {
            view.X = 0;
            view.Y = 0;
            view.Width = Dim.Fill();
            view.Height = Dim.Fill(1);
        }

        // Start with dashboard visible
        _activeView = _dashboardTab;
        _activeIndex = 0;
        _calendarTab.Visible = false;
        _emailTab.Visible = false;

        Add(_dashboardTab, _calendarTab, _emailTab, _statusLabel, _batteryLabel);

        // Remove the default Window Esc → QuitToplevel binding
        KeyBindings.Remove(Key.Esc);

        // Global keybindings
        KeyDown += (_, e) =>
        {
            if (IsEditing()) return;

            if (e == new Key('1'))
            {
                SwitchToView(0);
                e.Handled = true;
            }
            else if (e == new Key('2'))
            {
                SwitchToView(1);
                e.Handled = true;
            }
            else if (e == new Key('3'))
            {
                SwitchToView(2);
                e.Handled = true;
            }
            else if (e == Key.Q)
            {
                App?.RequestStop();
                e.Handled = true;
            }
            else if (e == new Key('?'))
            {
                ShowHelp();
                e.Handled = true;
            }
        };

        // Wire WebSocket events
        _ws.OnMailSync += evt => App?.Invoke(() => HandleMailSync(evt));
        _ws.OnCalendarSync += evt => App?.Invoke(() => HandleCalendarSync(evt));
        _ws.OnStatusChange += evt => App?.Invoke(() => HandleStatusChange(evt));
        _ws.OnConnectionStateChanged += connected =>
            App?.Invoke(() => ShowStatus(connected ? "Connected to server" : "Disconnected from server"));

        // Start WS connection and initial data load after the view is initialized
        Initialized += (_, _) =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _ws.ConnectAsync(CancellationToken.None);
                }
                catch
                {
                    App?.Invoke(() => ShowError("Could not connect to WebSocket server"));
                }
            });
            _ = _dashboardTab.LoadAsync(CancellationToken.None);
            _ = LoadAccountColorsAsync();
        };
    }

    private void SwitchToView(int index)
    {
        if (index == _activeIndex) return;

        _activeView.Visible = false;
        _activeIndex = index;
        _activeView = index switch
        {
            1 => _calendarTab,
            2 => _emailTab,
            _ => _dashboardTab
        };
        _activeView.Visible = true;
        _activeView.SetFocus();
    }

    private async Task LoadAccountColorsAsync()
    {
        var accounts = await SafeApiCallAsync(c => _api.GetAccountsAsync(c));
        if (accounts is null) return;
        foreach (var a in accounts)
            _accountColors[a.Id] = a.Color;
    }

    internal string? GetAccountColor(string accountId) =>
        _accountColors.GetValueOrDefault(accountId);

    /// <summary>
    /// Returns a stable Color for the given account, using the configured hex color
    /// if available, otherwise assigning from the Solarized palette round-robin.
    /// Both AccountListView and EmailListView must call this to stay in sync.
    /// </summary>
    internal Color GetOrAssignAccountColor(string accountId)
    {
        if (_resolvedAccountColors.TryGetValue(accountId, out var cached))
            return cached;

        var hex = GetAccountColor(accountId);
        var color = hex is not null
            ? Sol.ParseHex(hex)
            : Sol.AccountPalette[_nextPaletteIdx++ % Sol.AccountPalette.Length];
        _resolvedAccountColors[accountId] = color;
        return color;
    }

    /// <summary>
    /// Disables type-ahead and registers the Q-to-quit shortcut on a ListView.
    /// </summary>
    internal void RegisterQuitKey(ListView list)
    {
        list.KeystrokeNavigator.Matcher = new NoTypeAheadMatcher();
        list.KeyDown += (_, e) =>
        {
            if (e == Key.Q && !IsEditing())
            {
                App?.RequestStop();
                e.Handled = true;
            }
        };
    }

    internal void RegisterQuitKey(View view)
    {
        view.KeyDown += (_, e) =>
        {
            if (e == Key.Q && !IsEditing())
            {
                App?.RequestStop();
                e.Handled = true;
            }
        };
    }

    internal bool IsAccountOnline(string accountId) =>
        !_accountStatus.TryGetValue(accountId, out var online) || online;

    internal void UpdateBattery(PowerInfo? power)
    {
        if (power is null || power.BatteryPercent == -1)
        {
            _batteryLabel.Text = "";
            return;
        }

        var remaining = power.TimeRemaining is not null ? $" ({power.TimeRemaining})" : "";
        _batteryLabel.Text = $"\u26a1 {power.BatteryPercent}%{remaining}";
    }

    internal void ShowError(string message)
    {
        _statusLabel.Text = $"Error: {message}";
        ClearStatusAfterDelay();
    }

    internal void ShowStatus(string message)
    {
        _statusLabel.Text = message;
        ClearStatusAfterDelay();
    }

    private void ClearStatusAfterDelay()
    {
        if (_statusClearTimeout is not null)
            App?.RemoveTimeout(_statusClearTimeout);

        _statusClearTimeout = App?.AddTimeout(TimeSpan.FromSeconds(5), () =>
        {
            _statusLabel.Text = "Ready  [1] Dashboard  [2] Calendar  [3] Email";
            _statusClearTimeout = null;
            return false;
        });
    }

    internal async Task<T?> SafeApiCallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct = default)
    {
        try
        {
            return await call(ct);
        }
        catch (TaskCanceledException)
        {
            return default;
        }
        catch (Exception ex)
        {
            App?.Invoke(() => ShowError(ex.Message));
            return default;
        }
    }

    internal async Task SafeApiCallAsync(Func<CancellationToken, Task> call, CancellationToken ct = default)
    {
        try
        {
            await call(ct);
        }
        catch (TaskCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            App?.Invoke(() => ShowError(ex.Message));
        }
    }

    private void ShowHelp()
    {
        var help = _activeIndex switch
        {
            1 => FormatHelp("Calendar", [
                ("1/2/3",      "Switch view"),
                ("Up/Down",    "Move cursor through time"),
                ("Left/Right", "Move between day columns"),
                ("",           "(shifts window at edges)"),
                ("PgUp/PgDn",  "Scroll by page"),
                ("T",          "Jump to current time"),
                ("N",          "New event at cursor time"),
                ("Enter",      "Edit event at cursor"),
                ("Q",          "Quit"),
                ("?",          "This help"),
            ]),
            2 => FormatHelp("Email", [
                ("1/2/3",      "Switch view"),
                ("Up/Down",    "Navigate messages"),
                ("Right",      "Enter reader pane"),
                ("Left/Esc",   "Back to inbox list"),
                ("N",          "Compose new email"),
                ("R",          "Reply to selected"),
                ("U",          "Filter unread"),
                ("F",          "Filter flagged"),
                ("Space",      "Toggle read/unread"),
                ("!",          "Toggle flag"),
                ("J",          "Mark as junk"),
                ("D",          "Download attachment"),
                ("/",          "Search"),
                ("Q",          "Quit"),
                ("?",          "This help"),
            ]),
            _ => FormatHelp("Dashboard", [
                ("1/2/3",   "Switch view"),
                ("Up/Down", "Navigate lists"),
                ("Q",       "Quit"),
                ("?",       "This help"),
            ]),
        };

        if (App is not null)
            MessageBox.Query(App, "Help", help, ["OK"]);
    }

    private static string FormatHelp(string title, (string key, string desc)[] entries)
    {
        var keyWidth = entries.Where(e => e.key.Length > 0).Max(e => e.key.Length);
        var lines = new List<string>();
        var header = $"{title} Keys:";
        lines.Add(header);
        lines.Add("");
        foreach (var (key, desc) in entries)
        {
            if (key.Length == 0)
                lines.Add($"  {new string(' ', keyWidth)}  {desc}");
            else
                lines.Add($"  {key.PadRight(keyWidth)}  {desc}");
        }

        // Pad all lines to the same length so MessageBox doesn't re-wrap
        var maxLen = lines.Max(l => l.Length);
        for (var i = 0; i < lines.Count; i++)
            lines[i] = lines[i].PadRight(maxLen);

        return string.Join("\n", lines);
    }

    private bool IsEditing()
    {
        var focused = MostFocused;
        return focused is TextField or TextView;
    }

    internal void NotifyMailChanged()
    {
        _ = _dashboardTab.RefreshMailOverviewAsync(CancellationToken.None);
    }

    private void HandleMailSync(MailSyncEvent evt)
    {
        _ = _emailTab.RefreshInboxAsync(CancellationToken.None);
        _ = _dashboardTab.RefreshMailOverviewAsync(CancellationToken.None);
    }

    private void HandleCalendarSync(CalendarSyncEvent evt)
    {
        _ = _calendarTab.RefreshAsync(CancellationToken.None);
        _ = _dashboardTab.RefreshAgendaAsync(CancellationToken.None);
    }

    private void HandleStatusChange(StatusChangeEvent evt)
    {
        _accountStatus[evt.AccountId] = evt.Online;
        var status = evt.Online ? "online" : "OFFLINE";
        ShowStatus($"Account '{evt.AccountId}' is now {status}");
        _dashboardTab.UpdateAccountStatus(evt.AccountId, evt.Online);
        _emailTab.UpdateAccountStatus(evt.AccountId, evt.Online);
    }
}
