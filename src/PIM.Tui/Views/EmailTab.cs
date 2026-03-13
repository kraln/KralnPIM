using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly AccountListView _accountList;
    private readonly EmailListView _emailList;
    private readonly TextField _searchField;
    private readonly View _readerContent;
    private readonly Label _readerFromLabel;
    private readonly Label _readerToLabel;
    private readonly Label _readerDateLabel;
    private readonly Label _readerSubjectLabel;
    private readonly TextView _readerBody;
    private readonly ListView _attachmentList;

    private List<EmailHeader> _allEmails = [];
    private List<EmailHeader> _emails = [];
    private List<AccountOverview> _accounts = [];
    private EmailHeader? _selectedEmail;
    private MailDetail? _currentDetail;
    private ComposeView? _composeView;

    private bool? _filterUnread;
    private bool? _filterFlagged;
    private int _offset;
    private bool _staleInbox;
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

        _accountList = new AccountListView(app)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };
        _accountList.FilterChanged += ApplyAccountFilter;
        _app.RegisterQuitKey(_accountList);

        _emailList = new EmailListView(app)
        {
            X = 0, Y = Pos.Bottom(_accountList),
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        _emailList.SelectionChanged += header =>
        {
            if (header is not null)
                _ = SelectEmailAsync(header);
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
            _emailList.SetFocus();
            e.Handled = true;
        };

        _inboxFrame.Add(_accountList, _emailList, _searchField);

        // Right pane: reader
        _readerFrame = new FrameView
        {
            Title = "Reader",
            X = Pos.Right(_inboxFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = false
        };

        // Wrap reader children in a container View
        _readerContent = new View
        {
            X = 0, Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            CanFocus = false
        };

        _readerFromLabel = new Label { X = 0, Y = 0, Width = Dim.Fill(), Height = 1, Text = "" };
        _readerToLabel = new Label { X = 0, Y = 1, Width = Dim.Fill(), Height = 1, Text = "" };
        _readerDateLabel = new Label { X = 0, Y = 2, Width = Dim.Fill(), Height = 1, Text = "" };
        _readerSubjectLabel = new Label { X = 0, Y = 3, Width = Dim.Fill(), Height = 1, Text = "" };

        _readerBody = new TextView
        {
            X = 0, Y = 5,
            Width = Dim.Fill(),
            Height = Dim.Fill(3),
            ReadOnly = false, // Terminal.Gui v2 bug: ReadOnly=true prevents rendering
            WordWrap = true,
            CanFocus = false
        };

        _attachmentList = new ListView
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
            Height = 3,
            Visible = false,
            CanFocus = false
        };

        _attachmentList.KeyDown += (_, e) =>
        {
            if (e == Key.CursorLeft || e == Key.Esc)
            {
                _attachmentList.CanFocus = false;
                _readerBody.CanFocus = false;
                _readerContent.CanFocus = false;
                _readerFrame.CanFocus = false;
                _emailList.SetFocus();
                e.Handled = true;
            }
        };

        _attachmentList.Accepting += (_, e) =>
        {
            if (_selectedEmail is null) return;
            var idx = _attachmentList.SelectedItem ?? -1;
            if (idx >= 0 && idx < _selectedEmail.Attachments.Count)
            {
                _ = DownloadAttachmentAsync(_selectedEmail.MessageId,
                    _selectedEmail.Attachments[idx].Filename);
                e.Handled = true;
            }
        };

        _readerBody.KeyDown += (_, e) =>
        {
            if (e == Key.CursorLeft || e == Key.Esc)
            {
                _attachmentList.CanFocus = false;
                _readerBody.CanFocus = false;
                _readerContent.CanFocus = false;
                _readerFrame.CanFocus = false;
                _readerFrame.Title = "Reader";
                _emailList.SetFocus();
                e.Handled = true;
            }
            else if (e == Key.CursorDown)
            {
                _readerBody.ScrollVertical(1);
                UpdateReaderScrollIndicator();
                e.Handled = true;
            }
            else if (e == Key.CursorUp)
            {
                _readerBody.ScrollVertical(-1);
                UpdateReaderScrollIndicator();
                e.Handled = true;
            }
            else if (e == Key.PageDown)
            {
                _readerBody.ScrollVertical(_readerBody.Viewport.Height);
                UpdateReaderScrollIndicator();
                e.Handled = true;
            }
            else if (e == Key.PageUp)
            {
                _readerBody.ScrollVertical(-_readerBody.Viewport.Height);
                UpdateReaderScrollIndicator();
                e.Handled = true;
            }
        };

        _readerContent.Add(_readerFromLabel, _readerToLabel, _readerDateLabel,
            _readerSubjectLabel, _readerBody, _attachmentList);
        _readerFrame.Add(_readerContent);

        Add(_inboxFrame, _readerFrame);

        // Right from inbox enters reader; Left from reader returns to inbox.
        KeyDown += (_, e) =>
        {
            if (_composeView is not null)
            {
                if (e == Key.Esc)
                {
                    CloseCompose();
                    e.Handled = true;
                }
                return;
            }

            if ((e == Key.CursorRight || e == Key.Tab) && _emailList.HasFocus && _currentDetail is not null)
            {
                _readerFrame.CanFocus = true;
                _readerContent.CanFocus = true;
                _readerBody.CanFocus = true;
                _readerBody.SetFocus();
                UpdateReaderScrollIndicator();
                e.Handled = true;
            }
            else if (e == Key.CursorLeft && (_readerBody.HasFocus || _attachmentList.HasFocus))
            {
                _attachmentList.CanFocus = false;
                _readerBody.CanFocus = false;
                _readerContent.CanFocus = false;
                _readerFrame.CanFocus = false;
                _emailList.SetFocus();
                e.Handled = true;
            }
            else if (e == Key.Esc)
            {
                _attachmentList.CanFocus = false;
                _readerBody.CanFocus = false;
                _readerContent.CanFocus = false;
                _readerFrame.CanFocus = false;
                _emailList.SetFocus();
                e.Handled = true;
            }
        };

        // Refresh stale data when inbox regains focus
        _emailList.HasFocusChanged += (_, e) =>
        {
            if (!e.NewValue) return;
            if (_staleInbox)
                _ = RefreshInboxAsync(CancellationToken.None, force: true);
            else if (_currentDetail is null && _emailList.SelectedEmail is { } sel)
                _ = SelectEmailAsync(sel);
        };

        // Key handlers on the email list (fires after EmailListView's own handler)
        _emailList.KeyDown += (_, e) =>
        {
            if (e.Handled) return;

            if (e == Key.CursorLeft)
            {
                e.Handled = true;
            }
            else if (e == Key.U)
            {
                _filterUnread = _filterUnread is null ? false : null;
                _offset = 0;
                _ = RefreshInboxAsync(CancellationToken.None, force: true);
                e.Handled = true;
            }
            else if (e == Key.F)
            {
                _filterFlagged = _filterFlagged is null ? true : null;
                _offset = 0;
                _ = RefreshInboxAsync(CancellationToken.None, force: true);
                e.Handled = true;
            }
            else if (e == Key.Space)
            {
                if (_selectedEmail is not null)
                    _ = ToggleReadAsync(_selectedEmail);
                e.Handled = true;
            }
            else if (e == new Key('!'))
            {
                if (_selectedEmail is not null)
                    _ = ToggleFlagAsync(_selectedEmail);
                e.Handled = true;
            }
            else if (e == Key.J)
            {
                if (_selectedEmail is not null)
                    _ = MoveToJunkAsync(_selectedEmail);
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
                _ = RefreshInboxAsync(CancellationToken.None, force: true);
                e.Handled = true;
            }
            else if (e == Key.PageUp)
            {
                _offset = Math.Max(0, _offset - PageSize);
                _ = RefreshInboxAsync(CancellationToken.None, force: true);
                e.Handled = true;
            }
            else if (e == Key.Q)
            {
                _app.App?.RequestStop();
                e.Handled = true;
            }
        };

        // Load initial data when tab becomes visible
        Initialized += (_, _) => _ = RefreshInboxAsync(CancellationToken.None, force: true);
    }

    internal async Task RefreshInboxAsync(CancellationToken ct, bool force = false)
    {
        if (!force && _emailList.HasFocus)
        {
            _staleInbox = true;
            return;
        }

        var accountsTask = _app.SafeApiCallAsync(c => _api.GetAccountsAsync(c), ct);
        var emailsTask = _app.SafeApiCallAsync(
            c => _api.ListMailAsync(isRead: _filterUnread, isFlagged: _filterFlagged,
                offset: _offset, limit: PageSize, ct: c), ct);

        await Task.WhenAll(accountsTask, emailsTask);

        var accounts = await accountsTask;
        var emails = await emailsTask;

        if (accounts is not null)
            _accounts = accounts.Where(a => a.Type is not "CalDav").ToList();
        if (emails is not null)
            _allEmails = emails;

        _staleInbox = false;
        _app.App?.Invoke(() =>
        {
            UpdateAccountList();
            ApplyAccountFilter();

            var filterHints = new List<string>();
            if (_filterUnread == false) filterHints.Add("unread");
            if (_filterFlagged == true) filterHints.Add("flagged");
            _inboxFrame.Title = filterHints.Count > 0
                ? $"Inbox ({string.Join(", ", filterHints)})"
                : "Inbox";

            DeferFocusToInbox();
        });
    }

    private void UpdateAccountList()
    {
        _accountList.SetAccounts(_accounts);
        _accountList.Height = Math.Max(1, _accounts.Count);
    }

    private void ApplyAccountFilter()
    {
        var disabled = _accountList.DisabledAccountIds;
        _emails = disabled.Count > 0
            ? _allEmails.Where(m => !disabled.Contains(m.AccountId)).ToList()
            : _allEmails;

        var selectedIdx = _emailList.SelectedIndex;
        _emailList.SetEmails(_emails);
        if (selectedIdx >= 0 && selectedIdx < _emails.Count)
            _emailList.SelectedIndex = selectedIdx;
    }

    private async Task SelectEmailAsync(EmailHeader header)
    {
        _selectedEmail = header;

        var detail = await _app.SafeApiCallAsync(
            c => _api.GetMailDetailAsync(header.MessageId, c));

        if (detail is null)
        {
            // 404 — message was deleted/moved on server; remove from local list.
            // RemoveEmail handles removal from the shared list, so don't also RemoveAt here.
            var gone = _emails.IndexOf(header);
            if (gone >= 0)
            {
                _selectedEmail = null;
                _currentDetail = null;
                _app.App?.Invoke(() =>
                {
                    _emailList.RemoveEmail(gone);
                    ClearReader();
                    DeferFocusToInbox();
                });
            }
            return;
        }

        _currentDetail = detail;

        _app.App?.Invoke(() =>
        {
            _readerFrame.CanFocus = false;
            _readerBody.CanFocus = false;
            _attachmentList.CanFocus = false;

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

            _readerContent.SetNeedsDraw();
            DeferFocusToInbox();
        });

        // Auto mark-as-read
        if (!header.IsRead)
        {
            var idx = _emails.IndexOf(header);
            if (idx >= 0)
            {
                _emails[idx] = header with { IsRead = true };
                _selectedEmail = _emails[idx];
            }
            _app.App?.Invoke(() =>
            {
                RefreshInboxListDisplayInPlace();
                DeferFocusToInbox();
            });
            await _app.SafeApiCallAsync(
                c => _api.SetMailFlagsAsync(header.MessageId, new MailFlagPatch(true, null), c));
        }
    }

    private async Task ToggleReadAsync(EmailHeader header)
    {
        var newRead = !header.IsRead;
        await _app.SafeApiCallAsync(
            c => _api.SetMailFlagsAsync(header.MessageId, new MailFlagPatch(newRead, null), c));

        var idx = _emails.IndexOf(header);
        if (idx >= 0)
        {
            _emails[idx] = header with { IsRead = newRead };
            _selectedEmail = _emails[idx];
            _app.App?.Invoke(() =>
            {
                RefreshInboxListDisplayInPlace();
                DeferFocusToInbox();
            });
        }

        _app.ShowStatus(newRead ? "Marked as read" : "Marked as unread");
        _app.NotifyMailChanged();
    }

    private async Task ToggleFlagAsync(EmailHeader header)
    {
        var newFlagged = !header.IsFlagged;
        await _app.SafeApiCallAsync(
            c => _api.SetMailFlagsAsync(header.MessageId, new MailFlagPatch(null, newFlagged), c));

        var idx = _emails.IndexOf(header);
        if (idx >= 0)
        {
            _emails[idx] = header with { IsFlagged = newFlagged };
            _selectedEmail = _emails[idx];
            _app.App?.Invoke(() =>
            {
                RefreshInboxListDisplayInPlace();
                DeferFocusToInbox();
            });
        }

        _app.ShowStatus(newFlagged ? "Flagged" : "Unflagged");
        _app.NotifyMailChanged();
    }

    private async Task MoveToJunkAsync(EmailHeader header)
    {
        await _app.SafeApiCallAsync(
            c => _api.MoveToJunkAsync(header.MessageId, c));

        // RemoveEmail handles removal from the shared list, so don't also RemoveAt here.
        var idx = _emails.IndexOf(header);
        if (idx >= 0)
        {
            _selectedEmail = null;
            _currentDetail = null;
            _app.App?.Invoke(() =>
            {
                _emailList.RemoveEmail(idx);
                ClearReader();
                DeferFocusToInbox();
            });
        }

        _app.ShowStatus("Moved to junk");
        _app.NotifyMailChanged();
    }

    private void ClearReader()
    {
        _readerFromLabel.Text = "";
        _readerToLabel.Text = "";
        _readerDateLabel.Text = "";
        _readerSubjectLabel.Text = "";
        _readerBody.Text = "";
        _attachmentList.Visible = false;
    }

    private void RefreshInboxListDisplayInPlace()
    {
        for (var i = 0; i < _emails.Count; i++)
            _emailList.UpdateEmail(i, _emails[i]);
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
            _emailList.SetEmails(_emails);
            DeferFocusToInbox();
        });
    }

    private void OpenCompose(MailDetail? replyTo)
    {
        _composeView = new ComposeView(_api, _app, replyTo, onClose: CloseCompose);

        _readerContent.Visible = false;
        _readerFrame.CanFocus = true;
        _readerFrame.Add(_composeView);
        _composeView.SetFocus();
    }

    private void CloseCompose()
    {
        if (_composeView is null) return;
        _readerFrame.Remove(_composeView);
        _composeView = null;

        _readerContent.Visible = true;
        _attachmentList.Visible = _selectedEmail?.Attachments.Count > 0;
        DeferFocusToInbox();
    }

    private async Task DownloadAttachmentAsync(string messageId, string filename)
    {
        var result = await _app.SafeApiCallAsync(
            c => _api.DownloadAttachmentAsync(messageId, filename, c));

        if (result is null) return;

        var filePath = result.FilePath.Replace("~",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        if (!File.Exists(filePath))
        {
            _app.ShowError($"File not found: {filePath}");
            return;
        }

        _app.ShowStatus($"Opening: {filePath}");

        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _app.ShowError($"Could not open file: {ex.Message}");
        }
    }

    internal void UpdateAccountStatus(string accountId, bool online)
    {
        _ = RefreshInboxAsync(CancellationToken.None);
    }

    private void DeferFocusToInbox() =>
        _app.App?.Invoke(() => _emailList.SetFocus());

    private void UpdateReaderScrollIndicator()
    {
        var contentH = _readerBody.GetContentSize().Height;
        var viewportH = _readerBody.Viewport.Height;
        if (contentH <= viewportH)
        {
            _readerFrame.Title = "Reader";
            return;
        }
        var top = _readerBody.Viewport.Y;
        var pct = (int)(100.0 * (top + viewportH) / contentH);
        _readerFrame.Title = $"Reader [{Math.Min(pct, 100)}%]";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}
