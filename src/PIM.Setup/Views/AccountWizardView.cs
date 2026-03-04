using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using PIM.Core.Config;
using PIM.Setup.Auth;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Setup.Views;

internal sealed partial class AccountWizardView : View
{
    private readonly SetupApp _app;
    private readonly AccountConfig? _editing;
    private int _step;

    // Wizard state
    private AccountType _accountType;
    private string _id = "";
    private string _displayName = "";
    private string _imapHost = "";
    private int _imapPort = 993;
    private bool _imapTls = true;
    private string _smtpHost = "";
    private int _smtpPort = 587;
    private string _username = "";
    private string _clientId = "";
    private string _clientSecret = "";
    private string _tenantId = "";
    private string _password = "";
    private readonly List<CalendarSourceConfig> _calendars = [];

    public AccountWizardView(SetupApp app, AccountConfig? editing)
    {
        _app = app;
        _editing = editing;
        CanFocus = true;

        if (editing is not null)
        {
            _accountType = editing.Type;
            _id = editing.Id;
            _displayName = editing.DisplayName;
            _imapHost = editing.ImapHost ?? "";
            _imapPort = editing.ImapPort ?? 993;
            _imapTls = editing.ImapTls ?? true;
            _smtpHost = editing.SmtpHost ?? "";
            _smtpPort = editing.SmtpPort ?? 587;
            _username = editing.Username ?? "";
            _clientId = editing.ClientId ?? "";
            _clientSecret = editing.ClientSecret ?? "";
            _tenantId = editing.TenantId ?? "";
            if (editing.Calendars is not null)
                _calendars.AddRange(editing.Calendars);
            _step = 1; // Skip type selection when editing
        }

        // Defer rendering until App is available (needed for App?.Invoke calls in Render* methods)
        Initialized += (_, _) => RenderStep();
    }

    private int TotalSteps => _accountType switch
    {
        AccountType.Imap => 4,
        AccountType.CalDav => 4,
        _ => 3,
    };

    private void RenderStep()
    {
        RemoveAll();

        switch (_step)
        {
            case 0:
                RenderTypeSelection();
                break;
            case 1:
                RenderAccountDetails();
                break;
            case 2 when _accountType is AccountType.Imap or AccountType.CalDav:
                RenderPasswordStep();
                break;
            case 2:
                RenderAuthTestStep();
                break;
            case 3 when _accountType is AccountType.CalDav:
                RenderCalDavCalendarsStep();
                break;
            case 3:
                RenderAuthTestStep();
                break;
        }
    }

    private void RenderTypeSelection()
    {
        var title = new Label { X = 2, Y = 0, Text = $"Add Account                                   Step 1 of {TotalSteps}" };

        var prompt = new Label { X = 2, Y = 2, Text = "Choose account type:" };

        var typeOptions = new ObservableCollection<string>(
        [
            "IMAP / SMTP        Standard email server",
            "Google              Gmail + Google Calendar via OAuth",
            "Office 365          O365 Mail + Calendar via device code auth",
            "CalDAV              Standalone calendar server (Radicale, Nextcloud, etc.)",
        ]);

        var typeList = new ListView
        {
            X = 4, Y = 4,
            Width = Dim.Fill(4),
            Height = 4,
            Source = new ListWrapper<string>(typeOptions),
        };

        var next = new Button { X = Pos.AnchorEnd(22), Y = Pos.AnchorEnd(2), Text = "Next" };
        var cancel = new Button { X = Pos.AnchorEnd(10), Y = Pos.AnchorEnd(2), Text = "Cancel" };

        next.Accepting += (_, e) =>
        {
            e.Handled = true;
            _accountType = (typeList.SelectedItem ?? 0) switch
            {
                0 => AccountType.Imap,
                1 => AccountType.Google,
                2 => AccountType.Office365,
                3 => AccountType.CalDav,
                _ => AccountType.Imap,
            };
            _step = 1;
            RenderStep();
        };

        cancel.Accepting += (_, e) => { _app.ShowView(new AccountListView(_app)); e.Handled = true; };

        Add(title, prompt, typeList, next, cancel);
        App?.Invoke(() => typeList.SetFocus());
    }

    private void RenderAccountDetails()
    {
        var title = new Label { X = 2, Y = 0, Text = $"{_accountType} Account Details                    Step 2 of {TotalSteps}" };
        var y = 2;

        var idLabel = new Label { X = 2, Y = y, Text = "Account ID:" };
        var idField = new TextField { X = 16, Y = y, Width = 30, Text = _id };
        y += 2;

        var nameLabel = new Label { X = 2, Y = y, Text = "Display Name:" };
        var nameField = new TextField { X = 16, Y = y, Width = 30, Text = _displayName };
        y += 2;

        Add(title, idLabel, idField, nameLabel, nameField);

        switch (_accountType)
        {
            case AccountType.Imap:
                var imapHostLabel = new Label { X = 2, Y = y, Text = "IMAP Host:" };
                var imapHostField = new TextField { X = 16, Y = y, Width = 30, Text = _imapHost };
                y += 2;
                var imapPortLabel = new Label { X = 2, Y = y, Text = "IMAP Port:" };
                var imapPortField = new TextField { X = 16, Y = y, Width = 10, Text = _imapPort.ToString() };
                y += 2;
                var tlsLabel = new Label { X = 2, Y = y, Text = "Use TLS:" };
                var tlsCheck = new CheckBox
                {
                    X = 16, Y = y, Text = "",
                    Value = _imapTls ? CheckState.Checked : CheckState.UnChecked,
                };
                y += 2;
                var smtpHostLabel = new Label { X = 2, Y = y, Text = "SMTP Host:" };
                var smtpHostField = new TextField { X = 16, Y = y, Width = 30, Text = _smtpHost };
                y += 2;
                var smtpPortLabel = new Label { X = 2, Y = y, Text = "SMTP Port:" };
                var smtpPortField = new TextField { X = 16, Y = y, Width = 10, Text = _smtpPort.ToString() };
                y += 2;
                var userLabel = new Label { X = 2, Y = y, Text = "Username:" };
                var userField = new TextField { X = 16, Y = y, Width = 30, Text = _username };
                Add(imapHostLabel, imapHostField, imapPortLabel, imapPortField,
                    tlsLabel, tlsCheck, smtpHostLabel, smtpHostField,
                    smtpPortLabel, smtpPortField, userLabel, userField);

                AddNavigationButtons(y + 2, () =>
                {
                    _id = idField.Text;
                    _displayName = nameField.Text;
                    _imapHost = imapHostField.Text;
                    _ = int.TryParse(imapPortField.Text, out _imapPort);
                    _imapTls = tlsCheck.Value == CheckState.Checked;
                    _smtpHost = smtpHostField.Text;
                    _ = int.TryParse(smtpPortField.Text, out _smtpPort);
                    _username = userField.Text;
                    return ValidateDetails();
                });
                break;

            case AccountType.Google:
                var gClientLabel = new Label { X = 2, Y = y, Text = "Client ID:" };
                var gClientField = new TextField { X = 16, Y = y, Width = 45, Text = _clientId };
                y += 2;
                var gSecretLabel = new Label { X = 2, Y = y, Text = "Client Secret:" };
                var gSecretField = new TextField { X = 16, Y = y, Width = 30, Text = _clientSecret };
                Add(gClientLabel, gClientField, gSecretLabel, gSecretField);

                AddNavigationButtons(y + 2, () =>
                {
                    _id = idField.Text;
                    _displayName = nameField.Text;
                    _clientId = gClientField.Text;
                    _clientSecret = gSecretField.Text;
                    return ValidateDetails();
                });
                break;

            case AccountType.Office365:
                var tenantLabel = new Label { X = 2, Y = y, Text = "Tenant ID:" };
                var tenantField = new TextField { X = 16, Y = y, Width = 40, Text = _tenantId };
                y += 2;
                var o365ClientLabel = new Label { X = 2, Y = y, Text = "Client ID:" };
                var o365ClientField = new TextField { X = 16, Y = y, Width = 40, Text = _clientId };
                Add(tenantLabel, tenantField, o365ClientLabel, o365ClientField);

                AddNavigationButtons(y + 2, () =>
                {
                    _id = idField.Text;
                    _displayName = nameField.Text;
                    _tenantId = tenantField.Text;
                    _clientId = o365ClientField.Text;
                    return ValidateDetails();
                });
                break;

            case AccountType.CalDav:
                var caldavUserLabel = new Label { X = 2, Y = y, Text = "Username:" };
                var caldavUserField = new TextField { X = 16, Y = y, Width = 30, Text = _username };
                Add(caldavUserLabel, caldavUserField);

                AddNavigationButtons(y + 2, () =>
                {
                    _id = idField.Text;
                    _displayName = nameField.Text;
                    _username = caldavUserField.Text;
                    return ValidateDetails();
                });
                break;
        }

        App?.Invoke(() => idField.SetFocus());
    }

    private void RenderPasswordStep()
    {
        var title = new Label { X = 2, Y = 0, Text = $"Password                                      Step 3 of {TotalSteps}" };

        var prompt = new Label { X = 2, Y = 2, Text = $"Password for {_username}:" };
        var pwdField = new TextField { X = 2, Y = 3, Width = 40, Secret = true, Text = _password };

        var confirmLabel = new Label { X = 2, Y = 5, Text = "Confirm password:" };
        var confirmField = new TextField { X = 2, Y = 6, Width = 40, Secret = true, Text = _password };

        var note = new Label { X = 2, Y = 8, Text = "Password is stored in the local database, not in config.yaml." };

        Add(title, prompt, pwdField, confirmLabel, confirmField, note);

        AddNavigationButtons(10, () =>
        {
            if (string.IsNullOrEmpty(pwdField.Text))
            {
                _app.ShowError("Password is required.");
                return false;
            }
            if (pwdField.Text != confirmField.Text)
            {
                _app.ShowError("Passwords do not match.");
                return false;
            }
            _password = pwdField.Text;
            return true;
        });

        App?.Invoke(() => pwdField.SetFocus());
    }

    private void RenderCalDavCalendarsStep()
    {
        var title = new Label { X = 2, Y = 0, Text = $"CalDAV Calendars                              Step 4 of {TotalSteps}" };

        var listItems = new ObservableCollection<string>(
            _calendars.Select(c => $"  {c.Id,-20} {c.Url}"));
        var listView = new ListView
        {
            X = 2, Y = 2,
            Width = Dim.Fill(2),
            Height = 6,
            Source = new ListWrapper<string>(listItems),
        };

        var addBtn = new Button { X = 2, Y = 9, Text = "Add Calendar" };
        var removeBtn = new Button { X = 20, Y = 9, Text = "Remove Selected" };

        var idLabel = new Label { X = 2, Y = 11, Text = "Calendar ID:" };
        var idField = new TextField { X = 16, Y = 11, Width = 25 };
        var urlLabel = new Label { X = 2, Y = 12, Text = "CalDAV URL:" };
        var urlField = new TextField { X = 16, Y = 12, Width = 50 };
        var okBtn = new Button { X = 16, Y = 13, Text = "OK" };

        // Initially hide the add form
        idLabel.Visible = false;
        idField.Visible = false;
        urlLabel.Visible = false;
        urlField.Visible = false;
        okBtn.Visible = false;

        addBtn.Accepting += (_, e) =>
        {
            e.Handled = true;
            idField.Text = "";
            urlField.Text = "";
            idLabel.Visible = urlLabel.Visible = idField.Visible = urlField.Visible = okBtn.Visible = true;
            idField.SetFocus();
        };

        okBtn.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (string.IsNullOrWhiteSpace(idField.Text) || string.IsNullOrWhiteSpace(urlField.Text))
            {
                _app.ShowError("Calendar ID and URL are required.");
                return;
            }
            if (_calendars.Any(c => c.Id == idField.Text))
            {
                _app.ShowError($"Calendar ID '{idField.Text}' already exists.");
                return;
            }
            _calendars.Add(new CalendarSourceConfig(idField.Text, CalendarType.CalDav, urlField.Text));
            idLabel.Visible = urlLabel.Visible = idField.Visible = urlField.Visible = okBtn.Visible = false;
            listItems.Add($"  {idField.Text,-20} {urlField.Text}");
            listView.Source = new ListWrapper<string>(listItems);
        };

        removeBtn.Accepting += (_, e) =>
        {
            e.Handled = true;
            var idx = listView.SelectedItem ?? -1;
            if (idx >= 0 && idx < _calendars.Count)
            {
                _calendars.RemoveAt(idx);
                listItems.RemoveAt(idx);
                listView.Source = new ListWrapper<string>(listItems);
            }
        };

        Add(title, listView, addBtn, removeBtn, idLabel, idField, urlLabel, urlField, okBtn);

        // Navigation: Back, Done (saves + runs test), Cancel
        var back = new Button { X = Pos.AnchorEnd(34), Y = Pos.AnchorEnd(2), Text = "Back" };
        var done = new Button { X = Pos.AnchorEnd(22), Y = Pos.AnchorEnd(2), Text = "Done" };
        var cancel = new Button { X = Pos.AnchorEnd(10), Y = Pos.AnchorEnd(2), Text = "Cancel" };

        back.Accepting += (_, e) => { _step--; RenderStep(); e.Handled = true; };
        done.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (_calendars.Count == 0)
            {
                _app.ShowError("At least one calendar is required for CalDAV accounts.");
                return;
            }
            SaveAccount();
            _app.ShowView(new AccountListView(_app));
        };
        cancel.Accepting += (_, e) => { _app.ShowView(new AccountListView(_app)); e.Handled = true; };

        Add(back, done, cancel);
    }

    private void RenderAuthTestStep()
    {
        var stepNum = TotalSteps;
        var title = new Label { X = 2, Y = 0, Text = $"Authenticate & Test                           Step {stepNum} of {stepNum}" };

        var statusText = new TextView
        {
            X = 2, Y = 2,
            Width = Dim.Fill(2),
            Height = Dim.Fill(4),
            ReadOnly = true,
        };

        var runBtn = new Button { X = 2, Y = Pos.AnchorEnd(2), Text = "Run All" };
        var skipBtn = new Button { X = 16, Y = Pos.AnchorEnd(2), Text = "Skip" };
        var backBtn = new Button { X = 28, Y = Pos.AnchorEnd(2), Text = "Back" };
        var cancelBtn = new Button { X = Pos.AnchorEnd(10), Y = Pos.AnchorEnd(2), Text = "Cancel" };

        void AppendStatus(string msg)
        {
            App?.Invoke(() =>
            {
                statusText.Text += msg + "\n";
                statusText.MoveEnd();
            });
        }

        runBtn.Accepting += (_, e) =>
        {
            e.Handled = true;
            SaveAccount();
            statusText.Text = "";

            _ = Task.Run(async () =>
            {
                try
                {
                    _app.InitializeDb();
                    AppendStatus("[OK] Database initialized");

                    if (_app.AuthRepo is null)
                    {
                        AppendStatus("[FAIL] No database connection");
                        return;
                    }

                    switch (_accountType)
                    {
                        case AccountType.Imap:
                            await _app.AuthRepo.SaveImapPasswordAsync(_id, _password);
                            AppendStatus("[OK] Password saved");
                            AppendStatus("[ ] IMAP/SMTP connection test — run PIM.Server to verify");
                            break;

                        case AccountType.CalDav:
                            await _app.AuthRepo.SaveCalDavPasswordAsync(_id, _password);
                            AppendStatus("[OK] Password saved");
                            AppendStatus("[ ] CalDAV connection test — run PIM.Server to verify");
                            break;

                        case AccountType.Google:
                            AppendStatus("[ ] Starting Google OAuth...");
                            var googleOk = await GoogleAuthFlow.AuthorizeAsync(
                                _clientId, _clientSecret, _id,
                                _app.AuthRepo, AppendStatus, CancellationToken.None);
                            AppendStatus(googleOk ? "[OK] Google token acquired" : "[FAIL] Google auth failed");
                            break;

                        case AccountType.Office365:
                            AppendStatus("[ ] Starting O365 device code flow...");
                            var graphOk = await GraphAuthFlow.AuthorizeAsync(
                                _clientId, _tenantId, _id,
                                _app.AuthRepo, AppendStatus, CancellationToken.None);
                            AppendStatus(graphOk ? "[OK] O365 token acquired" : "[FAIL] O365 auth failed");
                            break;
                    }

                    AppendStatus("\nDone. Press [Skip] or [Back] to continue.");
                }
                catch (Exception ex)
                {
                    AppendStatus($"[FAIL] {ex.Message}");
                }
            });
        };

        skipBtn.Accepting += (_, e) =>
        {
            e.Handled = true;
            SaveAccount();
            _app.ShowView(new AccountListView(_app));
        };

        backBtn.Accepting += (_, e) => { _step--; RenderStep(); e.Handled = true; };
        cancelBtn.Accepting += (_, e) => { _app.ShowView(new AccountListView(_app)); e.Handled = true; };

        Add(title, statusText, runBtn, skipBtn, backBtn, cancelBtn);
    }

    private void AddNavigationButtons(int y, Func<bool> validate)
    {
        var hasBack = _step > 0 && _editing is null || _step > 1;

        if (hasBack)
        {
            var back = new Button { X = Pos.AnchorEnd(34), Y = Pos.AnchorEnd(2), Text = "Back" };
            back.Accepting += (_, e) => { _step--; RenderStep(); e.Handled = true; };
            Add(back);
        }

        var next = new Button { X = Pos.AnchorEnd(22), Y = Pos.AnchorEnd(2), Text = "Next" };
        var cancel = new Button { X = Pos.AnchorEnd(10), Y = Pos.AnchorEnd(2), Text = "Cancel" };

        next.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (validate())
            {
                _step++;
                RenderStep();
            }
        };

        cancel.Accepting += (_, e) => { _app.ShowView(new AccountListView(_app)); e.Handled = true; };

        Add(next, cancel);
    }

    private bool ValidateDetails()
    {
        if (string.IsNullOrWhiteSpace(_id))
        {
            _app.ShowError("Account ID is required.");
            return false;
        }

        if (!AccountIdRegex().IsMatch(_id))
        {
            _app.ShowError("Account ID must be lowercase letters, numbers, and hyphens only.");
            return false;
        }

        // Check uniqueness (allow same ID if editing that account)
        var duplicate = _app.Config.Accounts
            .Any(a => a.Id == _id && (_editing is null || a.Id != _editing.Id));
        if (duplicate)
        {
            _app.ShowError($"Account ID '{_id}' already exists.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_displayName))
        {
            _app.ShowError("Display Name is required.");
            return false;
        }

        switch (_accountType)
        {
            case AccountType.Imap:
                if (string.IsNullOrWhiteSpace(_imapHost)) { _app.ShowError("IMAP Host is required."); return false; }
                if (_imapPort is <= 0 or > 65535) { _app.ShowError("IMAP Port must be 1-65535."); return false; }
                if (string.IsNullOrWhiteSpace(_smtpHost)) { _app.ShowError("SMTP Host is required."); return false; }
                if (_smtpPort is <= 0 or > 65535) { _app.ShowError("SMTP Port must be 1-65535."); return false; }
                if (string.IsNullOrWhiteSpace(_username)) { _app.ShowError("Username is required."); return false; }
                break;

            case AccountType.Google:
                if (string.IsNullOrWhiteSpace(_clientId)) { _app.ShowError("Client ID is required."); return false; }
                if (string.IsNullOrWhiteSpace(_clientSecret)) { _app.ShowError("Client Secret is required."); return false; }
                break;

            case AccountType.Office365:
                if (string.IsNullOrWhiteSpace(_tenantId)) { _app.ShowError("Tenant ID is required."); return false; }
                if (string.IsNullOrWhiteSpace(_clientId)) { _app.ShowError("Client ID is required."); return false; }
                break;

            case AccountType.CalDav:
                if (string.IsNullOrWhiteSpace(_username)) { _app.ShowError("Username is required."); return false; }
                break;
        }

        return true;
    }

    private void SaveAccount()
    {
        var account = new AccountConfig(
            Id: _id,
            Type: _accountType,
            DisplayName: _displayName,
            ImapHost: _accountType == AccountType.Imap ? _imapHost : null,
            ImapPort: _accountType == AccountType.Imap ? _imapPort : null,
            ImapTls: _accountType == AccountType.Imap ? _imapTls : null,
            SmtpHost: _accountType == AccountType.Imap ? _smtpHost : null,
            SmtpPort: _accountType == AccountType.Imap ? _smtpPort : null,
            Username: _accountType is AccountType.Imap or AccountType.CalDav ? _username : null,
            ClientId: _accountType is AccountType.Google or AccountType.Office365 ? _clientId : null,
            ClientSecret: _accountType == AccountType.Google ? _clientSecret : null,
            TenantId: _accountType == AccountType.Office365 ? _tenantId : null,
            Calendars: _accountType == AccountType.CalDav ? _calendars.ToList() : null
        );

        var accounts = _app.Config.Accounts.ToList();

        if (_editing is not null)
        {
            var idx = accounts.FindIndex(a => a.Id == _editing.Id);
            if (idx >= 0)
                accounts[idx] = account;
            else
                accounts.Add(account);
        }
        else
        {
            accounts.Add(account);
        }

        _app.Config = _app.Config with { Accounts = accounts };
        _app.MarkChanged();
    }

    [GeneratedRegex(@"^[a-z0-9-]+$")]
    private static partial Regex AccountIdRegex();
}
