using System.Collections.ObjectModel;
using PIM.Core.Models;
using PIM.Tui.Client;
using PIM.Tui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Tui.Views;

internal sealed class EventEditorView : View
{
    private readonly PimApiClient _api;
    private readonly TuiApp _app;
    private readonly Action _onClose;
    private readonly CalendarEvent? _existing;

    private readonly TextField _summaryField;
    private readonly DateField _startDateField;
    private readonly TimeField _startTimeField;
    private readonly DateField _endDateField;
    private readonly TimeField _endTimeField;
    private readonly CheckBox _allDayCheck;
    private readonly ComboBox _accountCombo;
    private readonly TextField _locationField;
    private readonly TextField _inviteesField;
    private readonly TextView _descriptionView;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;

    private List<AccountOverview> _accounts = [];

    public EventEditorView(PimApiClient api, TuiApp app, CalendarEvent? existing, Action onClose)
    {
        _api = api;
        _app = app;
        _existing = existing;
        _onClose = onClose;
        CanFocus = true;

        X = 0; Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var title = existing is null ? "New Event" : "Edit Event";
        var now = DateTimeOffset.Now;

        var y = 0;

        Add(new Label { X = 0, Y = y, Text = title, Width = Dim.Fill() });
        y += 2;

        Add(new Label { X = 0, Y = y, Text = "Summary:" });
        _summaryField = new TextField { X = 12, Y = y, Width = Dim.Fill(), Text = existing?.Summary ?? "" };
        Add(_summaryField);
        y++;

        Add(new Label { X = 0, Y = y, Text = "Start:" });
        _startDateField = new DateField { X = 12, Y = y, Value = (existing?.Start ?? now).DateTime };
        _startTimeField = new TimeField { X = 26, Y = y, Value = (existing?.Start ?? now).TimeOfDay };
        Add(_startDateField, _startTimeField);
        y++;

        Add(new Label { X = 0, Y = y, Text = "End:" });
        _endDateField = new DateField { X = 12, Y = y, Value = (existing?.End ?? now.AddHours(1)).DateTime };
        _endTimeField = new TimeField { X = 26, Y = y, Value = (existing?.End ?? now.AddHours(1)).TimeOfDay };
        Add(_endDateField, _endTimeField);
        y++;

        _allDayCheck = new CheckBox
        {
            X = 12, Y = y,
            Text = "All Day",
            Value = existing?.IsAllDay == true
                ? CheckState.Checked
                : CheckState.UnChecked
        };
        Add(_allDayCheck);
        y++;

        Add(new Label { X = 0, Y = y, Text = "Account:" });
        _accountCombo = new ComboBox { X = 12, Y = y, Width = Dim.Fill() };
        Add(_accountCombo);
        y++;

        Add(new Label { X = 0, Y = y, Text = "Location:" });
        _locationField = new TextField { X = 12, Y = y, Width = Dim.Fill(), Text = existing?.Location ?? "" };
        Add(_locationField);
        y++;

        Add(new Label { X = 0, Y = y, Text = "Invitees:" });
        _inviteesField = new TextField
        {
            X = 12, Y = y, Width = Dim.Fill(),
            Text = existing is not null ? string.Join(", ", existing.Invitees) : ""
        };
        Add(_inviteesField);
        y++;

        Add(new Label { X = 0, Y = y, Text = "Description:" });
        y++;

        _descriptionView = new TextView
        {
            X = 0, Y = y,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            Text = existing?.Description ?? ""
        };
        Add(_descriptionView);

        _saveButton = new Button { X = 0, Y = Pos.AnchorEnd(1), Text = "Save (Ctrl+S)" };
        _cancelButton = new Button { X = Pos.Right(_saveButton) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel (Esc)" };

        _saveButton.Accepting += (_, e) => { _ = SaveAsync(); e.Handled = true; };
        _cancelButton.Accepting += (_, e) => { _onClose(); e.Handled = true; };

        Add(_saveButton, _cancelButton);

        // Keybindings
        KeyDown += (_, e) =>
        {
            if (e == Key.S.WithCtrl)
            {
                _ = SaveAsync();
                e.Handled = true;
            }
            else if (e == Key.Esc)
            {
                _onClose();
                e.Handled = true;
            }
        };

        Initialized += (_, _) => _ = LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        _accounts = await _app.SafeApiCallAsync(c => _api.GetAccountsAsync(c)) ?? [];

        App?.Invoke(() =>
        {
            _accountCombo.SetSource(new ObservableCollection<string>(
                _accounts.Select(a => a.DisplayName)));

            if (_existing is not null)
            {
                var idx = _accounts.FindIndex(a => a.Id == _existing.AccountId);
                if (idx >= 0) _accountCombo.SelectedItem = idx;
            }
            else if (_accounts.Count > 0)
            {
                _accountCombo.SelectedItem = 0;
            }
        });
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_summaryField.Text))
        {
            _app.ShowError("Summary is required");
            return;
        }

        if (_accounts.Count == 0 || _accountCombo.SelectedItem < 0)
        {
            _app.ShowError("No account selected");
            return;
        }

        var account = _accounts[_accountCombo.SelectedItem];

        if (!_app.IsAccountOnline(account.Id))
        {
            _app.ShowError($"Account '{account.DisplayName}' is offline");
            return;
        }

        var isAllDay = _allDayCheck.Value == CheckState.Checked;
        var startDate = _startDateField.Value ?? DateTime.Today;
        var endDate = _endDateField.Value ?? DateTime.Today;
        var start = new DateTimeOffset(startDate.Date + _startTimeField.Value,
            DateTimeOffset.Now.Offset);
        var end = new DateTimeOffset(endDate.Date + _endTimeField.Value,
            DateTimeOffset.Now.Offset);

        if (end <= start)
        {
            _app.ShowError("End time must be after start time");
            return;
        }

        var invitees = _inviteesField.Text
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => a.Contains('@'))
            .ToList();

        var eventId = _existing?.EventId ?? Guid.NewGuid().ToString();
        var calendarId = _existing?.CalendarId ?? account.Id;

        var evt = new CalendarEvent(
            eventId, account.Id, calendarId,
            _summaryField.Text,
            string.IsNullOrWhiteSpace(_descriptionView.Text) ? null : _descriptionView.Text,
            start, end, isAllDay,
            string.IsNullOrWhiteSpace(_locationField.Text) ? null : _locationField.Text,
            invitees,
            _existing?.RecurrenceRule,
            _existing?.Status ?? EventStatus.Confirmed);

        if (_existing is not null)
            await _app.SafeApiCallAsync(c => _api.UpdateEventAsync(evt, c));
        else
            await _app.SafeApiCallAsync(c => _api.CreateEventAsync(evt, c));

        _app.ShowStatus(_existing is not null ? "Event updated" : "Event created");
        _onClose();
    }
}
