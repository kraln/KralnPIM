using System.Collections.ObjectModel;
using PIM.Core.Models;
using PIM.Tui.Client;
using PIM.Tui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Tui.Views;

internal sealed class EmailTab : View
{
    private readonly PimApiClient _api;
    private readonly TuiApp _app;

    private readonly FrameView _inboxFrame;
    private readonly FrameView _readerFrame;
    private readonly ListView _inboxList;
    private readonly TextField _searchField;
    private readonly Label _readerFromLabel;
    private readonly Label _readerToLabel;
    private readonly Label _readerDateLabel;
    private readonly Label _readerSubjectLabel;
    private readonly TextView _readerBody;
    private readonly ListView _attachmentList;

    private List<EmailHeader> _emails = [];
    private EmailHeader? _selectedEmail;
    private MailDetail? _currentDetail;
    private ComposeView? _composeView;

    private bool? _filterUnread;
    private bool? _filterFlagged;
    private int _offset;
    private const int PageSize = 50;

    public EmailTab(PimApiClient api, TuiApp app)
    {
        _api = api;
        _app = app;
        CanFocus = true;
        X = 0; Y = 0; Width = Dim.Fill(); Height = Dim.Fill();

        // Left pane: inbox
        _inboxFrame = new FrameView
        {
            Title = "Inbox",
            X = 0, Y = 0,
            Width = Dim.Percent(35),
            Height = Dim.Fill()
        };

        _inboxList = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        _inboxList.ValueChanged += (_, e) =>
        {
            if (e.NewValue is int idx && idx >= 0 && idx < _emails.Count)
                _ = SelectEmailAsync(_emails[idx]);
        };

        _searchField = new TextField
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Visible = false
        };

        _searchField.Accepting += (_, e) =>
        {
            var query = _searchField.Text;
            if (!string.IsNullOrWhiteSpace(query))
                _ = SearchAsync(query);
            _searchField.Visible = false;
            _inboxList.SetFocus();
            e.Handled = true;
        };

        _inboxFrame.Add(_inboxList, _searchField);

        // Right pane: reader
        _readerFrame = new FrameView
        {
            Title = "Reader",
            X = Pos.Right(_inboxFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _readerFromLabel = new Label { X = 0, Y = 0, Width = Dim.Fill(), Text = "" };
        _readerToLabel = new Label { X = 0, Y = 1, Width = Dim.Fill(), Text = "" };
        _readerDateLabel = new Label { X = 0, Y = 2, Width = Dim.Fill(), Text = "" };
        _readerSubjectLabel = new Label { X = 0, Y = 3, Width = Dim.Fill(), Text = "" };

        _readerBody = new TextView
        {
            X = 0, Y = 5,
            Width = Dim.Fill(),
            Height = Dim.Fill(3),
            ReadOnly = true
        };

        _attachmentList = new ListView
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
            Height = 3,
            Visible = false
        };

        _readerFrame.Add(_readerFromLabel, _readerToLabel, _readerDateLabel,
            _readerSubjectLabel, _readerBody, _attachmentList);

        Add(_inboxFrame, _readerFrame);

        // Key handlers go on _inboxList (the focused control) so they fire before ListView's type-ahead search
        _inboxList.KeyDown += (_, e) =>
        {
            if (e == Key.U)
            {
                _filterUnread = _filterUnread is null ? false : null;
                _offset = 0;
                _ = RefreshInboxAsync(CancellationToken.None);
                e.Handled = true;
            }
            else if (e == Key.F)
            {
                _filterFlagged = _filterFlagged is null ? true : null;
                _offset = 0;
                _ = RefreshInboxAsync(CancellationToken.None);
                e.Handled = true;
            }
            else if (e == new Key('/'))
            {
                _searchField.Visible = true;
                _searchField.Text = "";
                _searchField.SetFocus();
                e.Handled = true;
            }
            else if (e == Key.R)
            {
                if (_currentDetail is not null && _composeView is null)
                {
                    OpenCompose(replyTo: _currentDetail);
                    e.Handled = true;
                }
            }
            else if (e == Key.N)
            {
                if (_composeView is null)
                {
                    OpenCompose(replyTo: null);
                    e.Handled = true;
                }
            }
            else if (e == Key.D)
            {
                if (_selectedEmail?.Attachments.Count > 0)
                {
                    var idx = _attachmentList.SelectedItem ?? -1;
                    if (idx >= 0 && idx < _selectedEmail.Attachments.Count)
                        _ = DownloadAttachmentAsync(_selectedEmail.MessageId,
                            _selectedEmail.Attachments[idx].Filename);
                    e.Handled = true;
                }
            }
            else if (e == Key.PageDown)
            {
                _offset += PageSize;
                _ = RefreshInboxAsync(CancellationToken.None);
                e.Handled = true;
            }
            else if (e == Key.PageUp)
            {
                _offset = Math.Max(0, _offset - PageSize);
                _ = RefreshInboxAsync(CancellationToken.None);
                e.Handled = true;
            }
        };
        _app.RegisterQuitKey(_inboxList);

        // Load initial data when tab becomes visible
        Initialized += (_, _) => _ = RefreshInboxAsync(CancellationToken.None);
    }

    internal async Task RefreshInboxAsync(CancellationToken ct)
    {
        var emails = await _app.SafeApiCallAsync(
            c => _api.ListMailAsync(isRead: _filterUnread, isFlagged: _filterFlagged,
                offset: _offset, limit: PageSize, ct: c), ct);

        if (emails is null) return;

        _emails = emails;
        _app.App?.Invoke(() =>
        {
            var filterHints = new List<string>();
            if (_filterUnread == false) filterHints.Add("unread");
            if (_filterFlagged == true) filterHints.Add("flagged");
            _inboxFrame.Title = filterHints.Count > 0
                ? $"Inbox ({string.Join(", ", filterHints)})"
                : "Inbox";

            _inboxList.SetSource(new ObservableCollection<string>(_emails
                .Select(m =>
                {
                    var indicator = m.IsRead ? " " : "*";
                    var flag = m.IsFlagged ? " [F]" : "";
                    var date = m.Date.ToLocalTime().ToString("MMM d");
                    var subject = Truncate(m.Subject ?? "(no subject)", 25);
                    var from = Truncate(m.FromDisplayName ?? m.FromAddress, 15);
                    return $"{indicator} {subject}{flag}  {from}  {date}";
                })));
        });
    }

    private async Task SelectEmailAsync(EmailHeader header)
    {
        _selectedEmail = header;

        var detail = await _app.SafeApiCallAsync(
            c => _api.GetMailDetailAsync(header.MessageId, c));

        if (detail is null) return;

        _currentDetail = detail;

        _app.App?.Invoke(() =>
        {
            _readerFromLabel.Text = $"From: {detail.Header.FromDisplayName} <{detail.Header.FromAddress}>";
            _readerToLabel.Text = $"To: {string.Join(", ", detail.Header.ToAddresses)}";
            _readerDateLabel.Text = $"Date: {detail.Header.Date.ToLocalTime():ddd, d MMM yyyy h:mm tt}";
            _readerSubjectLabel.Text = $"Subject: {detail.Header.Subject}";
            _readerBody.Text = detail.PlainTextBody ?? "(no body)";

            if (detail.Header.Attachments.Count > 0)
            {
                _attachmentList.Visible = true;
                _attachmentList.SetSource(new ObservableCollection<string>(
                    detail.Header.Attachments
                        .Select(a => $"  {a.Filename} ({FormatSize(a.SizeBytes)})")));
            }
            else
            {
                _attachmentList.Visible = false;
            }
        });

        // Auto mark-as-read
        if (!header.IsRead)
        {
            await _app.SafeApiCallAsync(
                c => _api.SetMailFlagsAsync(header.MessageId, new MailFlagPatch(true, null), c));
        }
    }

    private async Task SearchAsync(string query)
    {
        var result = await _app.SafeApiCallAsync(
            c => _api.SearchLocalAsync(query, "mail", c));

        if (result is null) return;

        _emails = result.Emails;
        _app.App?.Invoke(() =>
        {
            _inboxFrame.Title = $"Search: {query}";
            _inboxList.SetSource(new ObservableCollection<string>(_emails
                .Select(m =>
                {
                    var indicator = m.IsRead ? " " : "*";
                    return $"{indicator} {Truncate(m.Subject ?? "(no subject)", 25)}  {Truncate(m.FromDisplayName ?? m.FromAddress, 15)}";
                })));
        });
    }

    private void OpenCompose(MailDetail? replyTo)
    {
        _composeView = new ComposeView(_api, _app, replyTo, onClose: () =>
        {
            _readerFrame.Remove(_composeView!);
            _composeView = null;

            // Re-show reader content
            _readerFromLabel.Visible = true;
            _readerToLabel.Visible = true;
            _readerDateLabel.Visible = true;
            _readerSubjectLabel.Visible = true;
            _readerBody.Visible = true;
            _attachmentList.Visible = _selectedEmail?.Attachments.Count > 0;
        });

        // Hide reader, show compose
        _readerFromLabel.Visible = false;
        _readerToLabel.Visible = false;
        _readerDateLabel.Visible = false;
        _readerSubjectLabel.Visible = false;
        _readerBody.Visible = false;
        _attachmentList.Visible = false;

        _readerFrame.Add(_composeView);
        _composeView.SetFocus();
    }

    private async Task DownloadAttachmentAsync(string messageId, string filename)
    {
        var result = await _app.SafeApiCallAsync(
            c => _api.DownloadAttachmentAsync(messageId, filename, c));

        if (result is not null)
            _app.ShowStatus($"Downloaded: {result.FilePath}");
    }

    internal void UpdateAccountStatus(string accountId, bool online)
    {
        _ = RefreshInboxAsync(CancellationToken.None);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}
