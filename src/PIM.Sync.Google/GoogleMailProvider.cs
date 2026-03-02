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

        if (latestHistoryId is not null)
        {
            await _syncStateRepo.SetAsync(AccountId, ResourceType,
                DateTimeOffset.UtcNow, latestHistoryId, ct);
        }

        return new SyncResult<EmailHeader>(upserted, [], latestHistoryId);
    }

    private async Task<SyncResult<EmailHeader>> DeltaSyncAsync(string historyId, CancellationToken ct)
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
            return DecodeBase64Url(payload.Body.Data);

        // Walk parts recursively
        if (payload.Parts is not null)
        {
            // Prefer text/plain
            var plain = FindPart(payload.Parts, "text/plain");
            if (plain?.Body?.Data is not null)
                return DecodeBase64Url(plain.Body.Data);

            // Fall back to text/html → convert
            var html = FindPart(payload.Parts, "text/html");
            if (html?.Body?.Data is not null)
                return HtmlToTextConverter.Convert(DecodeBase64Url(html.Body.Data));
        }

        // Fallback: HTML body at top level
        if (payload.MimeType == "text/html" && payload.Body?.Data is not null)
            return HtmlToTextConverter.Convert(DecodeBase64Url(payload.Body.Data));

        return "";
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

    private static string DecodeBase64Url(string data) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(
            data.Replace('-', '+').Replace('_', '/')));

    private static string Base64UrlEncode(string data) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(data))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string BuildRfc2822Message(OutboundEmail email)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"From: {email.FromAccountId}");
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
