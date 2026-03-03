using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Setup.Views;

internal sealed class MainMenuView : View
{
    private readonly SetupApp _app;

    public MainMenuView(SetupApp app)
    {
        _app = app;

        var configExists = File.Exists(app.ConfigPath);
        var dbPath = app.Config.Storage.DbPath.Replace("~",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var dbExists = File.Exists(dbPath);
        var accountCount = app.Config.Accounts.Count;

        var header = new Label
        {
            X = 2, Y = 1,
            Text = $"Config: {app.ConfigPath}  [{(configExists ? "exists" : "new")}]\n" +
                   $"Database: {app.Config.Storage.DbPath}  [{(dbExists ? "exists" : "not found")}]\n" +
                   $"Accounts: {accountCount} configured"
        };

        var items = new ObservableCollection<string>(
        [
            "1. Accounts          Add, edit, remove, and test accounts",
            "2. UI Settings       Timezones",
            "3. System Settings   Weather location and provider",
            "4. Storage Settings  Database path, attachments, buffer window",
            "5. Server Settings   Listen address and ports",
            "───────────────────────────────────────────────────────",
            "6. Test All          Run connection tests for all accounts",
            "7. Save & Exit       Write config and exit",
            "8. Exit              Exit without saving",
        ]);

        var list = new ListView
        {
            X = 2, Y = 5,
            Width = Dim.Fill(2),
            Height = items.Count,
            Source = new ListWrapper<string>(items),
        };

        list.Accepting += (_, _) =>
        {
            HandleSelection(list.SelectedItem ?? -1);
        };

        Add(header, list);

        KeyDown += (_, e) =>
        {
            var idx = -1;
            if (e == new Key('1')) idx = 0;
            else if (e == new Key('2')) idx = 1;
            else if (e == new Key('3')) idx = 2;
            else if (e == new Key('4')) idx = 3;
            else if (e == new Key('5')) idx = 4;
            else if (e == new Key('6')) idx = 6;
            else if (e == new Key('7')) idx = 7;
            else if (e == new Key('8')) idx = 8;

            if (idx >= 0)
            {
                HandleSelection(idx);
                e.Handled = true;
            }
        };

        list.SetFocus();
    }

    private void HandleSelection(int index)
    {
        switch (index)
        {
            case 0:
                _app.ShowView(new AccountListView(_app));
                break;
            case 1:
                _app.ShowView(new SettingsView(_app, SettingsSection.Ui));
                break;
            case 2:
                _app.ShowView(new SettingsView(_app, SettingsSection.System));
                break;
            case 3:
                _app.ShowView(new SettingsView(_app, SettingsSection.Storage));
                break;
            case 4:
                _app.ShowView(new SettingsView(_app, SettingsSection.Server));
                break;
            case 5:
                // Separator — no action
                break;
            case 6:
                _app.ShowView(new ConnectionTestView(_app, null));
                break;
            case 7:
                if (_app.SaveConfig())
                    Application.RequestStop();
                break;
            case 8:
                _app.ConfirmExit();
                break;
        }
    }
}
