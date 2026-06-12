using System.Text.Json;
using MailKit;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using MimeKit;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.Sync.Imap;

public sealed class ImapMailProvider : IMailProvider
{
    private const string ResourceType = "mail";
    private const string FolderName = "INBOX";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ImapConnectionManager _connectionManager;
    private readonly ISyncStateRepository _syncStateRepo;
    private readonly ILogger<ImapMailProvider> _logger;
    private bool _authenticated;

    public string AccountId { get; }

    public ImapMailProvider(
        string accountId,
        ImapConnectionManager connectionManager,
        ISyncStateRepository syncStateRepo,
        ILogger<ImapMailProvider> logger)
    {
        AccountId = accountId;
        _connectionManager = connectionManager;
        _syncStateRepo = syncStateRepo;
        _logger = logger;
    }

    public async Task AuthenticateAsync(CancellationToken ct)
    {
        await _connectionManager.GetImapClientAsync(ct);
        _authenticated = true;
        _logger.LogInformation("IMAP authenticated for account {AccountId}", AccountId);
    }

    public async Task<SyncResult<EmailHeader>> SyncMailAsync(DateTimeOffset since, CancellationToken ct)
    {
        EnsureAuthenticated();
        var client = await _connectionManager.GetImapClientAsync(ct);
        var inbox = client.Inbox
            ?? throw new InvalidOperationException("IMAP server has no INBOX folder.");

        if (!inbox.IsOpen)
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        var (_, syncToken) = await _syncStateRepo.GetAsync(AccountId, ResourceType, ct);

        if (syncToken is not null)
        {
            var state = JsonSerializer.Deserialize<ImapSyncState>(syncToken, JsonOptions);
            if (state is not null)
            {
                if (state.UidValidity != inbox.UidValidity)
                {
                    _logger.LogWarning(
                        "UIDVALIDITY changed ({Old} -> {New}), performing full resync",
                        state.UidValidity, inbox.UidValidity);
                    return await FullSyncAsync(inbox, since, ct);
                }

                if (state.Modseq.HasValue && _connectionManager.SupportsCondstore)
                    return await CondstoreDeltaSyncAsync(inbox, state, ct);

                return await UidDeltaSyncAsync(inbox, state, ct);
            }
        }

        return await FullSyncAsync(inbox, since, ct);
    }

    public async Task<string> FetchBodyAsync(string messageId, CancellationToken ct)
    {
        EnsureAuthenticated();
        var client = await _connectionManager.GetImapClientAsync(ct);
        var inbox = client.Inbox
            ?? throw new InvalidOperationException("IMAP server has no INBOX folder.");

        if (!inbox.IsOpen)
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        var uid = new UniqueId(uint.Parse(messageId));
        var message = await inbox.GetMessageAsync(uid, ct);

        return ImapMailMapper.ExtractPlainText(message);
    }

    public async Task<string> DownloadAttachmentAsync(
        string messageId, string filename, string targetDir, CancellationToken ct)
    {
        EnsureAuthenticated();
        var client = await _connectionManager.GetImapClientAsync(ct);
        var inbox = client.Inbox
            ?? throw new InvalidOperationException("IMAP server has no INBOX folder.");

        if (!inbox.IsOpen)
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        var uid = new UniqueId(uint.Parse(messageId));
        var message = await inbox.GetMessageAsync(uid, ct);

        var attachment = message.Attachments
            .OfType<MimePart>()
            .FirstOrDefault(a => a.FileName == filename)
            ?? throw new InvalidOperationException(
                $"Attachment '{filename}' not found in message '{messageId}'.");

        var content = attachment.Content
            ?? throw new InvalidOperationException(
                $"Attachment '{filename}' in message '{messageId}' has no content body.");

        Directory.CreateDirectory(targetDir);
        var filePath = Path.Combine(targetDir, filename);

        await using var stream = File.Create(filePath);
        await content.DecodeToAsync(stream, ct);

        return filePath;
    }

    public async Task SendAsync(OutboundEmail email, CancellationToken ct)
    {
        EnsureAuthenticated();
        var mimeMessage = ImapMailMapper.ToMimeMessage(email);

        using var smtpClient = await _connectionManager.CreateSmtpClientAsync(ct);
        await smtpClient.SendAsync(mimeMessage, ct);
        await smtpClient.DisconnectAsync(true, ct);

        _logger.LogInformation("Sent email to {To}, subject: {Subject}",
            string.Join(", ", email.To), email.Subject);
    }

    public async Task<List<EmailHeader>> RemoteSearchAsync(string query, CancellationToken ct)
    {
        EnsureAuthenticated();
        var client = await _connectionManager.GetImapClientAsync(ct);
        var inbox = client.Inbox
            ?? throw new InvalidOperationException("IMAP server has no INBOX folder.");

        if (!inbox.IsOpen)
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        var searchQuery = SearchQuery.Or(
            SearchQuery.SubjectContains(query),
            SearchQuery.Or(
                SearchQuery.FromContains(query),
                SearchQuery.BodyContains(query)));

        var uids = await inbox.SearchAsync(searchQuery, ct);

        var limitedUids = uids.Take(50).ToList();

        if (limitedUids.Count == 0)
            return [];

        var items = MessageSummaryItems.Envelope | MessageSummaryItems.Flags
            | MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure;
        var summaries = await inbox.FetchAsync(limitedUids, items, ct);

        return summaries.Select(s => ImapMailMapper.ToEmailHeader(s, AccountId, FolderName)).ToList();
    }

    public async Task SetFlagsAsync(string messageId, bool? isRead, bool? isFlagged, CancellationToken ct)
    {
        EnsureAuthenticated();
        var client = await _connectionManager.GetImapClientAsync(ct);
        var inbox = client.Inbox
            ?? throw new InvalidOperationException("IMAP server has no INBOX folder.");

        if (!inbox.IsOpen || inbox.Access != FolderAccess.ReadWrite)
            await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

        var uid = new UniqueId(uint.Parse(messageId));

        if (isRead == true)
            await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
        else if (isRead == false)
            await inbox.RemoveFlagsAsync(uid, MessageFlags.Seen, true, ct);

        if (isFlagged == true)
            await inbox.AddFlagsAsync(uid, MessageFlags.Flagged, true, ct);
        else if (isFlagged == false)
            await inbox.RemoveFlagsAsync(uid, MessageFlags.Flagged, true, ct);
    }

    public async Task MoveToJunkAsync(string messageId, CancellationToken ct)
    {
        EnsureAuthenticated();
        var client = await _connectionManager.GetImapClientAsync(ct);
        var inbox = client.Inbox
            ?? throw new InvalidOperationException("IMAP server has no INBOX folder.");

        if (!inbox.IsOpen || inbox.Access != FolderAccess.ReadWrite)
            await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

        var uid = new UniqueId(uint.Parse(messageId));

        // Try standard Junk folder names
        IMailFolder? junkFolder = null;
        foreach (var name in new[] { "Junk", "Spam", "Junk E-mail" })
        {
            try
            {
                junkFolder = await client.GetFolderAsync(name, ct);
                break;
            }
            catch (FolderNotFoundException)
            {
            }
        }

        if (junkFolder is null)
            throw new InvalidOperationException("No Junk/Spam folder found on this IMAP server.");

        await inbox.MoveToAsync(uid, junkFolder, ct);
        _logger.LogInformation("Moved message {MessageId} to {Folder}", messageId, junkFolder.FullName);
    }

    private async Task<SyncResult<EmailHeader>> FullSyncAsync(
        IMailFolder inbox, DateTimeOffset since, CancellationToken ct)
    {
        var searchQuery = SearchQuery.DeliveredAfter(since.DateTime);
        var uids = await inbox.SearchAsync(searchQuery, ct);

        var items = MessageSummaryItems.Envelope | MessageSummaryItems.Flags
            | MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure;
        var summaries = uids.Count > 0
            ? await inbox.FetchAsync(uids, items, ct)
            : [];

        var upserted = summaries
            .Select(s => ImapMailMapper.ToEmailHeader(s, AccountId, FolderName))
            .ToList();

        uint maxUid = uids.Count > 0 ? uids.Max(u => u.Id) : 0;

        var newState = new ImapSyncState(
            inbox.UidValidity, maxUid,
            _connectionManager.SupportsCondstore ? inbox.HighestModSeq : null);

        var token = JsonSerializer.Serialize(newState, JsonOptions);
        await _syncStateRepo.SetAsync(AccountId, ResourceType, DateTimeOffset.UtcNow, token, ct);

        return new SyncResult<EmailHeader>(upserted, [], token);
    }

    private async Task<SyncResult<EmailHeader>> CondstoreDeltaSyncAsync(
        IMailFolder inbox, ImapSyncState state, CancellationToken ct)
    {
        var range = new UniqueIdRange(UniqueId.MinValue, UniqueId.MaxValue);

        var items = MessageSummaryItems.Envelope | MessageSummaryItems.Flags
            | MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure;
        var changed = await inbox.FetchAsync(range, state.Modseq!.Value, items, ct);

        var upserted = changed
            .Select(s => ImapMailMapper.ToEmailHeader(s, AccountId, FolderName))
            .ToList();

        uint maxUid = upserted.Count > 0
            ? Math.Max(state.MaxUid, upserted.Max(e => uint.Parse(e.MessageId)))
            : state.MaxUid;

        var newState = new ImapSyncState(inbox.UidValidity, maxUid, inbox.HighestModSeq);

        var token = JsonSerializer.Serialize(newState, JsonOptions);
        await _syncStateRepo.SetAsync(AccountId, ResourceType, DateTimeOffset.UtcNow, token, ct);

        return new SyncResult<EmailHeader>(upserted, [], token);
    }

    private async Task<SyncResult<EmailHeader>> UidDeltaSyncAsync(
        IMailFolder inbox, ImapSyncState state, CancellationToken ct)
    {
        var range = new UniqueIdRange(new UniqueId(state.MaxUid + 1), UniqueId.MaxValue);
        var searchQuery = SearchQuery.Uids(range);
        var uids = await inbox.SearchAsync(searchQuery, ct);

        var items = MessageSummaryItems.Envelope | MessageSummaryItems.Flags
            | MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure;
        var summaries = uids.Count > 0
            ? await inbox.FetchAsync(uids, items, ct)
            : [];

        var upserted = summaries
            .Select(s => ImapMailMapper.ToEmailHeader(s, AccountId, FolderName))
            .ToList();

        uint maxUid = uids.Count > 0
            ? Math.Max(state.MaxUid, uids.Max(u => u.Id))
            : state.MaxUid;

        var newState = new ImapSyncState(inbox.UidValidity, maxUid, null);

        var token = JsonSerializer.Serialize(newState, JsonOptions);
        await _syncStateRepo.SetAsync(AccountId, ResourceType, DateTimeOffset.UtcNow, token, ct);

        return new SyncResult<EmailHeader>(upserted, [], token);
    }

    private void EnsureAuthenticated()
    {
        if (!_authenticated)
            throw new InvalidOperationException("Call AuthenticateAsync before using the provider.");
    }

    internal sealed record ImapSyncState(
        uint UidValidity,
        uint MaxUid,
        ulong? Modseq);
}
