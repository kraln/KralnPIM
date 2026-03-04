using System.Collections.ObjectModel;
using PIM.Core.Models;
using PIM.Tui.Client;
using PIM.Tui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Tui.Views;

internal sealed class ComposeView : View
{
    private readonly PimApiClient _api;
    private readonly TuiApp _app;
    private readonly Action _onClose;
    private readonly string? _inReplyToMessageId;

    private readonly ComboBox _fromAccount;
    private readonly TextField _toField;
    private readonly TextField _ccField;
    private readonly TextField _bccField;
    private readonly TextField _subjectField;
    private readonly TextView _bodyView;
    private readonly Button _sendButton;
    private readonly Button _cancelButton;

    private List<AccountOverview> _accounts = [];

    public ComposeView(PimApiClient api, TuiApp app, MailDetail? replyTo, Action onClose)
    {
        _api = api;
        _app = app;
        _onClose = onClose;
        CanFocus = true;

        X = 0; Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var y = 0;

        Add(new Label { X = 0, Y = y, Text = "From:" });
        _fromAccount = new ComboBox { X = 8, Y = y, Width = Dim.Fill() };
        Add(_fromAccount);
        y++;

        Add(new Label { X = 0, Y = y, Text = "To:" });
        _toField = new TextField { X = 8, Y = y, Width = Dim.Fill() };
        Add(_toField);
        y++;

        Add(new Label { X = 0, Y = y, Text = "CC:" });
        _ccField = new TextField { X = 8, Y = y, Width = Dim.Fill() };
        Add(_ccField);
        y++;

        Add(new Label { X = 0, Y = y, Text = "BCC:" });
        _bccField = new TextField { X = 8, Y = y, Width = Dim.Fill() };
        Add(_bccField);
        y++;

        Add(new Label { X = 0, Y = y, Text = "Subject:" });
        _subjectField = new TextField { X = 8, Y = y, Width = Dim.Fill() };
        Add(_subjectField);
        y++;

        _bodyView = new TextView
        {
            X = 0, Y = y + 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            ReadOnly = false
        };
        Add(_bodyView);

        _sendButton = new Button { X = 0, Y = Pos.AnchorEnd(1), Text = "Send (Ctrl+S)" };
        _cancelButton = new Button { X = Pos.Right(_sendButton) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel (Esc)" };

        _sendButton.Accepting += (_, e) => { _ = SendAsync(); e.Handled = true; };
        _cancelButton.Accepting += (_, e) => { _onClose(); e.Handled = true; };

        Add(_sendButton, _cancelButton);

        // Pre-fill for reply
        if (replyTo is not null)
        {
            _inReplyToMessageId = replyTo.Header.MessageId;
            _toField.Text = replyTo.Header.FromAddress;
            _subjectField.Text = replyTo.Header.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                ? replyTo.Header.Subject
                : $"Re: {replyTo.Header.Subject}";

            if (replyTo.PlainTextBody is not null)
            {
                var quotedBody = string.Join("\n",
                    replyTo.PlainTextBody.Split('\n').Select(l => $"> {l}"));
                _bodyView.Text = $"\n\nOn {replyTo.Header.Date.ToLocalTime():ddd, d MMM yyyy h:mm tt}, " +
                    $"{replyTo.Header.FromDisplayName} wrote:\n{quotedBody}";
            }
        }

        // Keybindings
        KeyDown += (_, e) =>
        {
            if (e == Key.S.WithCtrl)
            {
                _ = SendAsync();
                e.Handled = true;
            }
            else if (e == Key.Esc)
            {
                _onClose();
                e.Handled = true;
            }
        };

        // Load accounts for From dropdown
        Initialized += (_, _) => _ = LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        _accounts = await _app.SafeApiCallAsync(
            c => _api.GetAccountsAsync(c)) ?? [];

        _app.App?.Invoke(() =>
        {
            _fromAccount.SetSource(new ObservableCollection<string>(
                _accounts.Select(a => $"{a.DisplayName} ({a.Type})")));
            if (_accounts.Count > 0)
                _fromAccount.SelectedItem = 0;
        });
    }

    private async Task SendAsync()
    {
        if (_accounts.Count == 0 || _fromAccount.SelectedItem < 0)
        {
            _app.ShowError("No account selected");
            return;
        }

        var accountId = _accounts[_fromAccount.SelectedItem].Id;

        if (!_app.IsAccountOnline(accountId))
        {
            _app.ShowError($"Account '{accountId}' is offline");
            return;
        }

        var to = ParseAddresses(_toField.Text);
        if (to.Count == 0)
        {
            _app.ShowError("At least one recipient is required");
            return;
        }

        var email = new OutboundEmail(
            accountId,
            to,
            ParseAddresses(_ccField.Text),
            ParseAddresses(_bccField.Text),
            _subjectField.Text,
            _bodyView.Text,
            _inReplyToMessageId);

        await _app.SafeApiCallAsync(c => _api.SendMailAsync(email, c));
        _app.ShowStatus("Message sent");
        _onClose();
    }

    private static List<string> ParseAddresses(string text) =>
        text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => a.Contains('@'))
            .ToList();
}
