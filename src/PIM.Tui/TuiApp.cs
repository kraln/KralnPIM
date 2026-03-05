using System.Collections.ObjectModel;
using PIM.Tui.Client;
using PIM.Tui.Models;
using PIM.Tui.Views;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

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
    private readonly TabView _tabs;
    private readonly Label _statusLabel;
    private readonly DashboardTab _dashboardTab;
    private readonly CalendarTab _calendarTab;
    private readonly EmailTab _emailTab;
    private readonly Dictionary<string, bool> _accountStatus = new();
    private object? _statusClearTimeout;

    public TuiApp(PimApiClient api, PimWsClient ws)
    {
        _api = api;
        _ws = ws;
        Title = "KralnPIM (q to quit)";

        _dashboardTab = new DashboardTab(api, this);
        _calendarTab = new CalendarTab(api, this);
        _emailTab = new EmailTab(api, this);

        _tabs = new TabView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        _tabs.AddTab(new Tab { DisplayText = "Dashboard", View = _dashboardTab }, false);
        _tabs.AddTab(new Tab { DisplayText = "Calendar", View = _calendarTab }, false);
        _tabs.AddTab(new Tab { DisplayText = "Email", View = _emailTab }, false);
        _tabs.SelectedTab = _tabs.Tabs.First();

        _statusLabel = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            Text = "Ready"
        };

        Add(_tabs, _statusLabel);

        // Remove the default Window Esc → QuitToplevel binding
        KeyBindings.Remove(Key.Esc);

        // Global keybindings (fires when no child view handles the key)
        KeyDown += (_, e) =>
        {
            if (e == Key.Q && !IsEditing())
            {
                App?.RequestStop();
                e.Handled = true;
            }
            else if (e == new Key('?') && !IsEditing())
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
        };
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

    internal bool IsAccountOnline(string accountId) =>
        !_accountStatus.TryGetValue(accountId, out var online) || online;

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
            _statusLabel.Text = "Ready";
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
        var activeTab = _tabs.SelectedTab;
        var tabName = activeTab?.DisplayText ?? "";

        var help = tabName switch
        {
            "Calendar" => string.Join("\n", [
                "Calendar Keys:",
                "",
                "  Up/Down     Move cursor through time",
                "  Left/Right  Move between day columns",
                "              (shifts window at edges)",
                "  Page Up/Dn  Scroll by page",
                "  T           Jump to current time",
                "  N           New event at cursor time",
                "  Enter       Edit event at cursor",
                "  Q           Quit",
                "  ?           This help"
            ]),
            "Email" => string.Join("\n", [
                "Email Keys:",
                "",
                "  Up/Down     Navigate messages",
                "  Enter       Open selected message",
                "  C           Compose new email",
                "  R           Reply to selected",
                "  /           Search",
                "  Q           Quit",
                "  ?           This help"
            ]),
            _ => string.Join("\n", [
                "Dashboard Keys:",
                "",
                "  Tab         Switch between tabs",
                "  Up/Down     Navigate lists",
                "  Q           Quit",
                "  ?           This help"
            ])
        };

        if (App is not null)
            MessageBox.Query(App, "Help", help, ["OK"]);
    }

    private bool IsEditing()
    {
        var focused = MostFocused;
        return focused is TextField or TextView;
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
