using System.Collections.ObjectModel;
using PIM.Core.Config;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Setup.Views;

/// <summary>Disables ListView type-ahead so letter keys reach KeyDown handlers.</summary>
internal sealed class NoTypeAheadMatcher : ICollectionNavigatorMatcher
{
    public bool IsCompatibleKey(Key key) => false;
    public bool IsMatch(string search, object value) => false;
}

internal sealed class AccountListView : View
{
    private readonly SetupApp _app;
    private readonly ListView _list;
    private readonly ObservableCollection<string> _items = [];
    private string? _pendingDeleteId;

    public AccountListView(SetupApp app)
    {
        _app = app;
        CanFocus = true;

        var header = new Label
        {
            X = 2, Y = 0,
            Text = "Accounts                                     [A]dd  [Esc] Back"
        };

        var columnHeader = new Label
        {
            X = 2, Y = 2,
            Text = "  Type       Display Name              Auth Status"
        };

        var separator = new Label
        {
            X = 2, Y = 3,
            Text = "  ──────────────────────────────────────────────────────────"
        };

        _list = new ListView
        {
            X = 2, Y = 4,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Source = new ListWrapper<string>(_items),
        };

        var footer = new Label
        {
            X = 2, Y = Pos.AnchorEnd(2),
            Text = "[E]dit  [T]est  [D]elete selected account"
        };

        // Disable type-ahead search so letter keys (A/E/T/D) reach our KeyDown handler
        _list.KeystrokeNavigator.Matcher = new NoTypeAheadMatcher();

        Add(header, columnHeader, separator, _list, footer);

        _list.Accepting += (_, e) => { EditSelected(); e.Handled = true; };

        // Key handlers go on _list (the focused control) so they fire before ListView's type-ahead search
        _list.KeyDown += (_, e) =>
        {
            if (e == Key.A)
            {
                _app.ShowView(new AccountWizardView(_app, null));
                e.Handled = true;
            }
            else if (e == Key.E)
            {
                EditSelected();
                e.Handled = true;
            }
            else if (e == Key.T)
            {
                TestSelected();
                e.Handled = true;
            }
            else if (e == Key.D)
            {
                DeleteSelected();
                e.Handled = true;
            }
        };

        Initialized += (_, _) =>
        {
            _ = RefreshListAsync();
        };
    }

    private async Task RefreshListAsync()
    {
        var lines = new List<string>();
        foreach (var account in _app.Config.Accounts)
        {
            var status = await _app.GetAuthStatusAsync(account);
            var typeStr = account.Type.ToString().PadRight(10);
            var nameStr = account.DisplayName.PadRight(25);
            lines.Add($"  {typeStr} {nameStr} {status}");
        }

        // Marshal UI update to main thread — async continuations run on thread pool
        App?.Invoke(() =>
        {
            _items.Clear();
            foreach (var line in lines)
                _items.Add(line);
        });
    }

    private void EditSelected()
    {
        var idx = _list.SelectedItem ?? -1;
        if (idx < 0 || idx >= _app.Config.Accounts.Count)
            return;

        var account = _app.Config.Accounts[idx];
        _app.ShowView(new AccountWizardView(_app, account));
    }

    private void TestSelected()
    {
        var idx = _list.SelectedItem ?? -1;
        if (idx < 0 || idx >= _app.Config.Accounts.Count)
            return;

        var account = _app.Config.Accounts[idx];
        _app.ShowView(new ConnectionTestView(_app, account));
    }

    private void DeleteSelected()
    {
        var idx = _list.SelectedItem ?? -1;
        if (idx < 0 || idx >= _app.Config.Accounts.Count)
            return;

        var account = _app.Config.Accounts[idx];

        // Two-press confirmation: first press shows warning, second press deletes
        if (_pendingDeleteId == account.Id)
        {
            _pendingDeleteId = null;
            PerformDelete(account, idx);
            return;
        }

        _pendingDeleteId = account.Id;
        _app.ShowStatus($"Press [D] again to confirm removal of '{account.Id}'");
    }

    private void PerformDelete(AccountConfig account, int idx)
    {
        var accounts = _app.Config.Accounts.ToList();
        accounts.RemoveAt(idx);
        _app.Config = _app.Config with { Accounts = accounts };
        _app.MarkChanged();

        // Clean up DB credentials
        if (_app.AuthRepo is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (account.Type is AccountType.Google or AccountType.Office365)
                        await _app.AuthRepo.DeleteOAuthTokenAsync(account.Id);
                    else if (account.Type is AccountType.Imap)
                        await _app.AuthRepo.DeletePasswordAsync(account.Id);
                    else if (account.Type is AccountType.CalDav)
                        await _app.AuthRepo.DeletePasswordAsync(account.Id);
                }
                catch { /* best effort */ }
            });
        }

        _ = RefreshListAsync();
        _app.ShowStatus($"Removed account '{account.Id}'");
    }
}
