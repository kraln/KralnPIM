using System.Text;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.Sync.Google;

public sealed class GoogleMailProvider : IMailProvider
{
    private const string ResourceType = "mail";
    private const string UserId = "me";
    private static readonly string[] MetadataHeaders = ["From", "To", "Cc", "Subject", "Date"];

    private readonly GoogleCredentialManager _credentialManager;
    private readonly ISyncStateRepository _syncStateRepo;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly ILogger<GoogleMailProvider> _logger;
    private GmailService? _service;

    public string AccountId { get; }

    public GoogleMailProvider(
        string accountId,
        GoogleCredentialManager credentialManager,
        ISyncStateRepository syncStateRepo,
        TokenBucketRateLimiter rateLimiter,
        ILogger<GoogleMailProvider> logger)
    {
        AccountId = accountId;
        _credentialManager = credentialManager;
        _syncStateRepo = syncStateRepo;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task AuthenticateAsync(CancellationToken ct)
    {
        var credential = await _credentialManager.EnsureAuthenticatedAsync(ct);
        _service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "KralnPIM",
        });
        _logger.LogInformation("Gmail authenticated for account {AccountId}", AccountId);
    }

    public async Task<SyncResult<EmailHeader>> SyncMailAsync(DateTimeOffset since, CancellationToken ct)
    {
        EnsureService();
        var (_, syncToken) = await _syncStateRepo.GetAsync(AccountId, ResourceType, ct);

        if (syncToken is not null)
            return await DeltaSyncAsync(syncToken, ct);

        return await FullSyncAsync(since, ct);
    }

    public async Task<string> FetchBodyAsync(string messageId, CancellationToken ct)
    {
        EnsureService();
        await _rateLimiter.WaitAsync(5, ct);

        var request = _service!.Users.Messages.Get(UserId, messageId);
        request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        var message = await request.ExecuteAsync(ct);

        return ExtractPlainText(message.Payload);
    }

    public async Task<string> DownloadAttachmentAsync(
        string messageId, string filename, string targetDir, CancellationToken ct)
    {
        EnsureService();
        await _rateLimiter.WaitAsync(5, ct);

        var request = _service!.Users.Messages.Get(UserId, messageId);
        request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        var message = await request.ExecuteAsync(ct);

        var part = FindAttachmentPart(message.Payload, filename)
            ?? throw new InvalidOperationException($"Attachment '{filename}' not found in message '{messageId}'.");

        await _rateLimiter.WaitAsync(5, ct);
        var attachment = await _service.Users.Messages.Attachments
            .Get(UserId, messageId, part.Body.AttachmentId)
            .ExecuteAsync(ct);

        var data = Convert.FromBase64String(
            attachment.Data.Replace('-', '+').Replace('_', '/'));

        Directory.CreateDirectory(targetDir);
        var filePath = Path.Combine(targetDir, filename);
        await File.WriteAllBytesAsync(filePath, data, ct);
        return filePath;
    }

    public async Task SendAsync(OutboundEmail email, CancellationToken ct)
    {
        EnsureService();
        await _rateLimiter.WaitAsync(100, ct);

        var rawMessage = BuildRfc2822Message(email);
        var encoded = Base64UrlEncode(rawMessage);

        var gmailMessage = new Message { Raw = encoded };
        await _service!.Users.Messages.Send(gmailMessage, UserId).ExecuteAsync(ct);

        _logger.LogInformation("Sent email to {To}, subject: {Subject}",
            string.Join(", ", email.To), email.Subject);
    }

    public async Task<List<EmailHeader>> RemoteSearchAsync(string query, CancellationToken ct)
    {
        EnsureService();
        var results = new List<EmailHeader>();

        await _rateLimiter.WaitAsync(5, ct);
        var listRequest = _service!.Users.Messages.List(UserId);
        listRequest.Q = query;
        listRequest.MaxResults = 50;
        var listResponse = await listRequest.ExecuteAsync(ct);

        if (listResponse.Messages is null)
            return results;

        foreach (var stub in listResponse.Messages)
        {
            await _rateLimiter.WaitAsync(5, ct);
            var getRequest = _service.Users.Messages.Get(UserId, stub.Id);
            getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            getRequest.MetadataHeaders = MetadataHeaders;
            var message = await getRequest.ExecuteAsync(ct);
            results.Add(GmailMapper.ToEmailHeader(message, AccountId));
        }

        return results;
    }

    public async Task SetFlagsAsync(string messageId, bool? isRead, bool? isFlagged, CancellationToken ct)
    {
        EnsureService();
        await _rateLimiter.WaitAsync(5, ct);

        var modify = new ModifyMessageRequest();
        modify.AddLabelIds ??= [];
        modify.RemoveLabelIds ??= [];

        if (isRead == true)
            modify.RemoveLabelIds.Add("UNREAD");
        else if (isRead == false)
            modify.AddLabelIds.Add("UNREAD");

        if (isFlagged == true)
            modify.AddLabelIds.Add("STARRED");
        else if (isFlagged == false)
            modify.RemoveLabelIds.Add("STARRED");

        await _service!.Users.Messages.Modify(modify, UserId, messageId).ExecuteAsync(ct);
    }

    public async Task MoveToJunkAsync(string messageId, CancellationToken ct)
    {
        EnsureService();
        await _rateLimiter.WaitAsync(5, ct);

        var modify = new ModifyMessageRequest
        {
            AddLabelIds = ["SPAM"],
            RemoveLabelIds = ["INBOX"]
        };
        await _service!.Users.Messages.Modify(modify, UserId, messageId).ExecuteAsync(ct);
        _logger.LogInformation("Moved Gmail message {MessageId} to SPAM", messageId);
    }

    private async Task<SyncResult<EmailHeader>> FullSyncAsync(DateTimeOffset since, CancellationToken ct)
    {
        var upserted = new List<EmailHeader>();
        string? pageToken = null;
        string? latestHistoryId = null;
        var sinceUnix = since.ToUnixTimeSeconds();

        do
        {
            await _rateLimiter.WaitAsync(5, ct);
            var listRequest = _service!.Users.Messages.List(UserId);
            listRequest.LabelIds = "INBOX";
            listRequest.Q = $"after:{sinceUnix}";
            listRequest.PageToken = pageToken;
            listRequest.MaxResults = 100;
            var listResponse = await listRequest.ExecuteAsync(ct);

            if (listResponse.Messages is not null)
            {
                foreach (var stub in listResponse.Messages)
                {
                    await _rateLimiter.WaitAsync(5, ct);
                    var getRequest = _service.Users.Messages.Get(UserId, stub.Id);
                    getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                    getRequest.MetadataHeaders = MetadataHeaders;
                    var message = await getRequest.ExecuteAsync(ct);

                    upserted.Add(GmailMapper.ToEmailHeader(message, AccountId));

                    if (message.HistoryId is not null)
                        latestHistoryId = message.HistoryId.Value.ToString();
                }
            }

            pageToken = listResponse.NextPageToken;
        } while (pageToken is not null);

        // Get the current history ID from the profile — message-level history IDs
        // can be stale and cause 404 on the next delta sync.
        await _rateLimiter.WaitAsync(5, ct);
        var profile = await _service!.Users.GetProfile(UserId).ExecuteAsync(ct);
        latestHistoryId = profile.HistoryId?.ToString() ?? latestHistoryId;

        if (latestHistoryId is not null)
        {
            await _syncStateRepo.SetAsync(AccountId, ResourceType,
                DateTimeOffset.UtcNow, latestHistoryId, ct);
        }

        return new SyncResult<EmailHeader>(upserted, [], latestHistoryId);
    }

    private async Task<SyncResult<EmailHeader>> DeltaSyncAsync(string historyId, CancellationToken ct)
    {
        try
        {
            return await DeltaSyncCoreAsync(historyId, ct);
        }
        catch (global::Google.GoogleApiException ex)
            when (ex.HttpStatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
        {
            // History ID expired — fall back to full sync
            _logger.LogWarning("Gmail history ID expired for {AccountId}, performing full sync", AccountId);
            return await FullSyncAsync(DateTimeOffset.UtcNow.AddDays(-30), ct);
        }
    }

    private async Task<SyncResult<EmailHeader>> DeltaSyncCoreAsync(string historyId, CancellationToken ct)
    {
        var upserted = new List<EmailHeader>();
        var deletedIds = new List<string>();
        string? pageToken = null;
        string? newHistoryId = historyId;

        do
        {
            await _rateLimiter.WaitAsync(5, ct);
            var request = _service!.Users.History.List(UserId);
            request.StartHistoryId = ulong.Parse(historyId);
            request.LabelId = "INBOX";
            request.HistoryTypes = UsersResource.HistoryResource.ListRequest.HistoryTypesEnum.MessageAdded;
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);

            if (response.HistoryId is not null)
                newHistoryId = response.HistoryId.Value.ToString();

            if (response.History is not null)
            {
                foreach (var record in response.History)
                {
                    if (record.MessagesAdded is not null)
                    {
                        foreach (var added in record.MessagesAdded)
                        {
                            await _rateLimiter.WaitAsync(5, ct);
                            var getRequest = _service.Users.Messages.Get(UserId, added.Message.Id);
                            getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                            getRequest.MetadataHeaders = MetadataHeaders;
                            var message = await getRequest.ExecuteAsync(ct);
                            upserted.Add(GmailMapper.ToEmailHeader(message, AccountId));
                        }
                    }

                    if (record.MessagesDeleted is not null)
                    {
                        foreach (var deleted in record.MessagesDeleted)
                            deletedIds.Add(deleted.Message.Id);
                    }
                }
            }

            pageToken = response.NextPageToken;
        } while (pageToken is not null);

        if (newHistoryId != historyId)
        {
            await _syncStateRepo.SetAsync(AccountId, ResourceType,
                DateTimeOffset.UtcNow, newHistoryId, ct);
        }

        return new SyncResult<EmailHeader>(upserted, deletedIds, newHistoryId);
    }

    private string ExtractPlainText(MessagePart? payload)
    {
        if (payload is null) return "";

        // Direct text/plain body
        if (payload.MimeType == "text/plain" && payload.Body?.Data is not null)
            return DecodePartBody(payload);

        // Walk parts recursively
        if (payload.Parts is not null)
        {
            // Prefer text/plain
            var plain = FindPart(payload.Parts, "text/plain");
            if (plain?.Body?.Data is not null)
                return DecodePartBody(plain);

            // Fall back to text/html → convert
            var html = FindPart(payload.Parts, "text/html");
            if (html?.Body?.Data is not null)
                return HtmlToTextConverter.Convert(DecodePartBody(html));
        }

        // Fallback: HTML body at top level
        if (payload.MimeType == "text/html" && payload.Body?.Data is not null)
            return HtmlToTextConverter.Convert(DecodePartBody(payload));

        return "";
    }

    /// <summary>
    /// Gmail API with format=full returns body data as base64url but does NOT decode
    /// the MIME content-transfer-encoding. If the part uses quoted-printable, we must
    /// decode that ourselves after the base64url decode.
    /// </summary>
    private static string DecodePartBody(MessagePart part)
    {
        // Gmail API base64url-decodes AND content-transfer-decodes (QP/base64)
        // for us — Body.Data is always the raw decoded bytes in the part's charset.
        var raw = DecodeBase64UrlBytes(part.Body!.Data!);
        var charset = GetCharset(part);
        Encoding encoding;
        if (charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        {
            encoding = Encoding.UTF8;
        }
        else
        {
            // Legacy code pages (Windows-1252, ISO-8859-*, etc.) require registration
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            encoding = Encoding.GetEncoding(charset);
        }
        return encoding.GetString(raw);
    }

    private static string GetCharset(MessagePart part)
    {
        var ct = part.Headers?.FirstOrDefault(
            h => string.Equals(h.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
        if (ct is null) return "utf-8";
        // Parse charset=X from Content-Type header
        var match = System.Text.RegularExpressions.Regex.Match(ct, @"charset=""?([^"";\s]+)""?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "utf-8";
    }

    private static MessagePart? FindPart(IList<MessagePart> parts, string mimeType)
    {
        foreach (var part in parts)
        {
            if (part.MimeType == mimeType)
                return part;
            if (part.Parts is not null)
            {
                var found = FindPart(part.Parts, mimeType);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static MessagePart? FindAttachmentPart(MessagePart? payload, string filename)
    {
        if (payload is null) return null;
        if (payload.Filename == filename && payload.Body?.AttachmentId is not null)
            return payload;
        if (payload.Parts is null) return null;
        foreach (var part in payload.Parts)
        {
            var found = FindAttachmentPart(part, filename);
            if (found is not null) return found;
        }
        return null;
    }

    private static byte[] DecodeBase64UrlBytes(string data) =>
        Convert.FromBase64String(data.Replace('-', '+').Replace('_', '/'));

    private static string DecodeBase64Url(string data) =>
        Encoding.UTF8.GetString(DecodeBase64UrlBytes(data));

    private static string Base64UrlEncode(string data) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(data))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string BuildRfc2822Message(OutboundEmail email)
    {
        var sb = new StringBuilder();
        var from = email.FromDisplayName is not null
            ? $"{email.FromDisplayName} <{email.FromAccountId}>"
            : email.FromAccountId;
        sb.AppendLine($"From: {from}");
        sb.AppendLine($"To: {string.Join(", ", email.To)}");
        if (email.Cc.Count > 0)
            sb.AppendLine($"Cc: {string.Join(", ", email.Cc)}");
        if (email.Bcc.Count > 0)
            sb.AppendLine($"Bcc: {string.Join(", ", email.Bcc)}");
        sb.AppendLine($"Subject: {email.Subject}");
        if (email.InReplyToMessageId is not null)
            sb.AppendLine($"In-Reply-To: {email.InReplyToMessageId}");
        sb.AppendLine("Content-Type: text/plain; charset=utf-8");
        sb.AppendLine();
        sb.Append(email.PlainTextBody);
        return sb.ToString();
    }

    private void EnsureService()
    {
        if (_service is null)
            throw new InvalidOperationException("Call AuthenticateAsync before using the provider.");
    }
}
