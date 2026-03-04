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
    private string _caldavUrl = "";
    private bool _ignoreSslErrors;
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
            _ignoreSslErrors = editing.IgnoreSslErrors ?? false;
            _caldavUrl = editing.CalDavUrl ?? "";
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
        _ => 4,
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
            case 3 when _accountType is AccountType.Imap:
                RenderAuthTestStep();
                break;
            case 3:
                RenderCalendarSelectionStep();
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
                y += 2;
                var imapSslLabel = new Label { X = 2, Y = y, Text = "Ignore SSL Errors:" };
                var imapSslCheck = new CheckBox
                {
                    X = 22, Y = y, Text = "",
                    Value = _ignoreSslErrors ? CheckState.Checked : CheckState.UnChecked,
                };
                Add(imapHostLabel, imapHostField, imapPortLabel, imapPortField,
                    tlsLabel, tlsCheck, smtpHostLabel, smtpHostField,
                    smtpPortLabel, smtpPortField, userLabel, userField,
                    imapSslLabel, imapSslCheck);

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
                    _ignoreSslErrors = imapSslCheck.Value == CheckState.Checked;
                    return ValidateDetails();
                }, [idField, nameField, imapHostField, imapPortField, smtpHostField, smtpPortField, userField]);
                break;

            case AccountType.Google:
                var gNote = new Label { X = 2, Y = y, Width = Dim.Fill(2), Text = "Uses built-in Google OAuth credentials — no API registration needed." };
                Add(gNote);

                AddNavigationButtons(y + 2, () =>
                {
                    _id = idField.Text;
                    _displayName = nameField.Text;
                    return ValidateDetails();
                }, [idField, nameField]);
                break;

            case AccountType.Office365:
                var o365Note = new Label { X = 2, Y = y, Width = Dim.Fill(2), Text = "Uses built-in O365 OAuth credentials — no API registration needed." };
                Add(o365Note);

                AddNavigationButtons(y + 2, () =>
                {
                    _id = idField.Text;
                    _displayName = nameField.Text;
                    return ValidateDetails();
                }, [idField, nameField]);
                break;

            case AccountType.CalDav:
                var caldavUrlLabel = new Label { X = 2, Y = y, Text = "Server URL:" };
                var caldavUrlField = new TextField { X = 16, Y = y, Width = 50, Text = _caldavUrl };
                y += 2;
                var caldavUserLabel = new Label { X = 2, Y = y, Text = "Username:" };
                var caldavUserField = new TextField { X = 16, Y = y, Width = 30, Text = _username };
                y += 2;
                var caldavSslLabel = new Label { X = 2, Y = y, Text = "Ignore SSL Errors:" };
                var caldavSslCheck = new CheckBox
                {
                    X = 22, Y = y, Text = "",
                    Value = _ignoreSslErrors ? CheckState.Checked : CheckState.UnChecked,
                };
                Add(caldavUrlLabel, caldavUrlField, caldavUserLabel, caldavUserField,
                    caldavSslLabel, caldavSslCheck);

                AddNavigationButtons(y + 2, () =>
                {
                    _id = idField.Text;
                    _displayName = nameField.Text;
                    _caldavUrl = caldavUrlField.Text;
                    _username = caldavUserField.Text;
                    _ignoreSslErrors = caldavSslCheck.Value == CheckState.Checked;
                    return ValidateDetails();
                }, [idField, nameField, caldavUrlField, caldavUserField]);
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
        }, [pwdField, confirmField]);

        App?.Invoke(() => pwdField.SetFocus());
    }

    private void RenderCalendarSelectionStep()
    {
        var stepNum = TotalSteps;
        var title = new Label { X = 2, Y = 0, Text = $"Calendar Selection                             Step {stepNum} of {stepNum}" };

        var statusLabel = new Label { X = 2, Y = 2, Text = "Discovering calendars..." };

        // Discovered calendars: (Id, Name, Url?, selected)
        var discovered = new List<(string Id, string Name, string? Url, bool Selected)>();
        var listItems = new ObservableCollection<string>();
        var listView = new ListView
        {
            X = 2, Y = 4,
            Width = Dim.Fill(2),
            Height = Dim.Fill(6),
            Source = new ListWrapper<string>(listItems),
        };
        listView.KeystrokeNavigator.Matcher = new NoTypeAheadMatcher();

        // Toggle selection on Enter or Space
        listView.Accepting += (_, e) =>
        {
            e.Handled = true;
            ToggleCalendarSelection(discovered, listItems, listView);
        };
        listView.KeyDown += (_, e) =>
        {
            if (e == Key.Space)
            {
                ToggleCalendarSelection(discovered, listItems, listView);
                e.Handled = true;
            }
        };

        var selectAllBtn = new Button { X = 2, Y = Pos.AnchorEnd(4), Text = "Select All" };
        var selectNoneBtn = new Button { X = 18, Y = Pos.AnchorEnd(4), Text = "Select None" };
        var addManualBtn = new Button { X = 36, Y = Pos.AnchorEnd(4), Text = "Add Manually" };
        addManualBtn.Visible = _accountType == AccountType.CalDav;

        selectAllBtn.Accepting += (_, e) =>
        {
            e.Handled = true;
            for (var i = 0; i < discovered.Count; i++)
            {
                discovered[i] = discovered[i] with { Selected = true };
                listItems[i] = FormatCalendarLine(discovered[i]);
            }
            listView.Source = new ListWrapper<string>(listItems);
        };

        selectNoneBtn.Accepting += (_, e) =>
        {
            e.Handled = true;
            for (var i = 0; i < discovered.Count; i++)
            {
                discovered[i] = discovered[i] with { Selected = false };
                listItems[i] = FormatCalendarLine(discovered[i]);
            }
            listView.Source = new ListWrapper<string>(listItems);
        };

        // Manual add form (CalDAV only)
        var manualIdLabel = new Label { X = 2, Y = Pos.AnchorEnd(6), Text = "ID:", Visible = false };
        var manualIdField = new TextField { X = 6, Y = Pos.AnchorEnd(6), Width = 20, Visible = false };
        var manualUrlLabel = new Label { X = 28, Y = Pos.AnchorEnd(6), Text = "URL:", Visible = false };
        var manualUrlField = new TextField { X = 33, Y = Pos.AnchorEnd(6), Width = 30, Visible = false };
        var manualOkBtn = new Button { X = 65, Y = Pos.AnchorEnd(6), Text = "OK", Visible = false };

        addManualBtn.Accepting += (_, e) =>
        {
            e.Handled = true;
            manualIdField.Text = "";
            manualUrlField.Text = "";
            manualIdLabel.Visible = manualIdField.Visible = true;
            manualUrlLabel.Visible = manualUrlField.Visible = true;
            manualOkBtn.Visible = true;
            manualIdField.SetFocus();
        };

        void AddManualCalendar()
        {
            if (string.IsNullOrWhiteSpace(manualIdField.Text) || string.IsNullOrWhiteSpace(manualUrlField.Text))
            {
                _app.ShowError("Calendar ID and URL are required.");
                return;
            }
            if (discovered.Any(d => d.Id == manualIdField.Text))
            {
                _app.ShowError($"Calendar '{manualIdField.Text}' already in the list.");
                return;
            }
            var entry = (manualIdField.Text, manualIdField.Text, (string?)manualUrlField.Text, true);
            discovered.Add(entry);
            listItems.Add(FormatCalendarLine(entry));
            listView.Source = new ListWrapper<string>(listItems);
            manualIdLabel.Visible = manualIdField.Visible = false;
            manualUrlLabel.Visible = manualUrlField.Visible = false;
            manualOkBtn.Visible = false;
        }

        manualOkBtn.Accepting += (_, e) => { e.Handled = true; AddManualCalendar(); };
        WireEnterAdvance([manualIdField, manualUrlField], AddManualCalendar);

        // Navigation
        var back = new Button { X = Pos.AnchorEnd(34), Y = Pos.AnchorEnd(2), Text = "Back" };
        var done = new Button { X = Pos.AnchorEnd(22), Y = Pos.AnchorEnd(2), Text = "Done" };
        var cancel = new Button { X = Pos.AnchorEnd(10), Y = Pos.AnchorEnd(2), Text = "Cancel" };

        back.Accepting += (_, e) => { _step--; RenderStep(); e.Handled = true; };
        done.Accepting += (_, e) =>
        {
            e.Handled = true;
            var selected = discovered.Where(d => d.Selected).ToList();
            if (_accountType == AccountType.CalDav && selected.Count == 0)
            {
                _app.ShowError("At least one calendar is required for CalDAV accounts.");
                return;
            }

            _calendars.Clear();
            var calType = _accountType switch
            {
                AccountType.Google => CalendarType.Google,
                AccountType.Office365 => CalendarType.Office365,
                _ => CalendarType.CalDav,
            };
            foreach (var cal in selected)
                _calendars.Add(new CalendarSourceConfig(cal.Id, calType, cal.Url));

            SaveAccount();
            SavePasswordToDb();
            CleanupDeselectedCalendars();
            _app.ShowView(new AccountListView(_app));
        };
        cancel.Accepting += (_, e) => { _app.ShowView(new AccountListView(_app)); e.Handled = true; };

        Add(title, statusLabel, listView, selectAllBtn, selectNoneBtn, addManualBtn,
            manualIdLabel, manualIdField, manualUrlLabel, manualUrlField, manualOkBtn,
            back, done, cancel);

        // Run discovery async
        _ = Task.Run(async () =>
        {
            try
            {
                _app.InitializeDb();
                var results = await DiscoverCalendarsForAccountAsync();

                var existingIds = new HashSet<string>(_calendars.Select(c => c.Id));

                App?.Invoke(() =>
                {
                    foreach (var (id, name, url) in results)
                    {
                        var selected = existingIds.Count == 0 || existingIds.Contains(id);
                        var entry = (id, name, url, selected);
                        discovered.Add(entry);
                        listItems.Add(FormatCalendarLine(entry));
                    }

                    // Also add any existing calendars not found by discovery (e.g. manually added)
                    foreach (var existing in _calendars.Where(c => !results.Any(r => r.Id == c.Id)))
                    {
                        var entry = (existing.Id, existing.Id, existing.Url, true);
                        discovered.Add(entry);
                        listItems.Add(FormatCalendarLine(entry));
                    }

                    listView.Source = new ListWrapper<string>(listItems);
                    statusLabel.Text = results.Count > 0
                        ? $"Found {results.Count} calendars. Space/Enter to toggle, then Done."
                        : "No calendars discovered. Add manually or check auth.";
                });
            }
            catch (Exception ex)
            {
                App?.Invoke(() =>
                {
                    statusLabel.Text = $"Discovery failed: {ex.Message}";
                    if (_accountType == AccountType.CalDav)
                        statusLabel.Text += " Use Add Manually.";
                });
            }
        });
    }

    private async Task<List<(string Id, string Name, string? Url)>> DiscoverCalendarsForAccountAsync()
    {
        switch (_accountType)
        {
            case AccountType.Google:
            {
                if (_app.AuthRepo is null)
                    throw new InvalidOperationException("No database available.");
                var gCid = string.IsNullOrWhiteSpace(_clientId)
                    ? PIM.Core.DefaultCredentials.Google.ClientId : _clientId;
                var gSec = string.IsNullOrWhiteSpace(_clientSecret)
                    ? PIM.Core.DefaultCredentials.Google.ClientSecret : _clientSecret;
                var results = await CalendarDiscovery.DiscoverGoogleCalendarsAsync(
                    _app.AuthRepo, _id, gCid, gSec, CancellationToken.None);
                return results.Select(r => (r.Id, r.Name, (string?)null)).ToList();
            }
            case AccountType.Office365:
            {
                if (_app.AuthRepo is null)
                    throw new InvalidOperationException("No database available.");
                var oCid = string.IsNullOrWhiteSpace(_clientId)
                    ? PIM.Core.DefaultCredentials.Office365.ClientId : _clientId;
                var oTid = string.IsNullOrWhiteSpace(_tenantId)
                    ? PIM.Core.DefaultCredentials.Office365.TenantId : _tenantId;
                var results = await CalendarDiscovery.DiscoverO365CalendarsAsync(
                    _app.AuthRepo, _id, oCid, oTid, CancellationToken.None);
                return results.Select(r => (r.Id, r.Name, (string?)null)).ToList();
            }
            case AccountType.CalDav:
            {
                if (string.IsNullOrWhiteSpace(_caldavUrl))
                    throw new InvalidOperationException("No server URL configured.");
                if (string.IsNullOrWhiteSpace(_username))
                    throw new InvalidOperationException("No username configured.");
                var password = _password;
                if (string.IsNullOrEmpty(password) && _app.AuthRepo is not null)
                    password = await _app.AuthRepo.GetCalDavPasswordAsync(_id) ?? "";
                if (string.IsNullOrEmpty(password))
                    throw new InvalidOperationException("No password available.");
                var results = await CalendarDiscovery.DiscoverCalDavCalendarsAsync(
                    _caldavUrl, _username, password, _ignoreSslErrors, CancellationToken.None);
                return results.Select(r => (r.Id, r.Name, (string?)r.Url)).ToList();
            }
            default:
                return [];
        }
    }

    private static void ToggleCalendarSelection(
        List<(string Id, string Name, string? Url, bool Selected)> discovered,
        ObservableCollection<string> listItems,
        ListView listView)
    {
        var idx = listView.SelectedItem ?? -1;
        if (idx < 0 || idx >= discovered.Count) return;
        discovered[idx] = discovered[idx] with { Selected = !discovered[idx].Selected };
        listItems[idx] = FormatCalendarLine(discovered[idx]);
        listView.Source = new ListWrapper<string>(listItems);
    }

    private static string FormatCalendarLine((string Id, string Name, string? Url, bool Selected) cal)
    {
        var check = cal.Selected ? "[x]" : "[ ]";
        var display = cal.Name.Length > 45 ? cal.Name[..45] + "..." : cal.Name;
        return $"  {check} {display}";
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
                            var gCid = string.IsNullOrWhiteSpace(_clientId) ? PIM.Core.DefaultCredentials.Google.ClientId : _clientId;
                            var gSec = string.IsNullOrWhiteSpace(_clientSecret) ? PIM.Core.DefaultCredentials.Google.ClientSecret : _clientSecret;
                            var googleOk = await GoogleAuthFlow.AuthorizeAsync(
                                gCid, gSec, _id,
                                _app.AuthRepo, AppendStatus, CancellationToken.None);
                            AppendStatus(googleOk ? "[OK] Google token acquired" : "[FAIL] Google auth failed");
                            break;

                        case AccountType.Office365:
                            AppendStatus("[ ] Starting O365 device code flow...");
                            var oCid = string.IsNullOrWhiteSpace(_clientId) ? PIM.Core.DefaultCredentials.Office365.ClientId : _clientId;
                            var oTid = string.IsNullOrWhiteSpace(_tenantId) ? PIM.Core.DefaultCredentials.Office365.TenantId : _tenantId;
                            var graphOk = await GraphAuthFlow.AuthorizeAsync(
                                oCid, oTid, _id,
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
            SavePasswordToDb();
            if (_step < TotalSteps - 1)
            {
                _step++;
                RenderStep();
            }
            else
            {
                _app.ShowView(new AccountListView(_app));
            }
        };

        backBtn.Accepting += (_, e) => { _step--; RenderStep(); e.Handled = true; };
        cancelBtn.Accepting += (_, e) => { _app.ShowView(new AccountListView(_app)); e.Handled = true; };

        Add(title, statusText, runBtn, skipBtn, backBtn, cancelBtn);
    }

    private void AddNavigationButtons(int y, Func<bool> validate, View[]? formFields = null)
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

        void Advance()
        {
            if (validate())
            {
                _step++;
                RenderStep();
            }
        }

        next.Accepting += (_, e) =>
        {
            e.Handled = true;
            Advance();
        };

        cancel.Accepting += (_, e) => { _app.ShowView(new AccountListView(_app)); e.Handled = true; };

        if (formFields is not null)
            WireEnterAdvance(formFields, Advance);

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
                break;

            case AccountType.Office365:
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
            ClientId: null,
            ClientSecret: null,
            TenantId: null,
            Calendars: _accountType is AccountType.CalDav or AccountType.Google or AccountType.Office365
                ? (_calendars.Count > 0 ? _calendars.ToList() : null) : null,
            IgnoreSslErrors: _ignoreSslErrors ? true : null,
            CalDavUrl: _accountType == AccountType.CalDav && !string.IsNullOrWhiteSpace(_caldavUrl) ? _caldavUrl : null
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

    private void CleanupDeselectedCalendars()
    {
        if (_calendars.Count == 0 || _app.CalendarRepo is null)
            return;

        var keepIds = _calendars.Select(c => c.Id).ToHashSet();
        _ = Task.Run(async () =>
        {
            try
            {
                var deleted = await _app.CalendarRepo.DeleteEventsNotInCalendarsAsync(_id, keepIds);
                if (deleted > 0)
                    App?.Invoke(() => _app.ShowStatus($"Cleaned up {deleted} events from deselected calendars."));
            }
            catch { /* best effort */ }
        });
    }

    private void SavePasswordToDb()
    {
        if (string.IsNullOrEmpty(_password) || _accountType is not (AccountType.Imap or AccountType.CalDav))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                _app.InitializeDb();
                if (_app.AuthRepo is null) return;

                if (_accountType == AccountType.Imap)
                    await _app.AuthRepo.SaveImapPasswordAsync(_id, _password);
                else
                    await _app.AuthRepo.SaveCalDavPasswordAsync(_id, _password);
            }
            catch (Exception ex)
            {
                App?.Invoke(() => _app.ShowError($"Failed to save password: {ex.Message}"));
            }
        });
    }

    /// <summary>
    /// Wires Enter on each field to advance focus to the next, with the last field triggering an action.
    /// </summary>
    private static void WireEnterAdvance(View[] fields, Action lastFieldAction)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            var nextField = i < fields.Length - 1 ? fields[i + 1] : null;
            fields[i].Accepting += (_, e) =>
            {
                e.Handled = true;
                if (nextField is not null)
                    nextField.SetFocus();
                else
                    lastFieldAction();
            };
        }
    }

    [GeneratedRegex(@"^[a-z0-9-]+$")]
    private static partial Regex AccountIdRegex();
}
