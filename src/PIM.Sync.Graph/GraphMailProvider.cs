using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Search.Query;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.Sync.Graph;

public sealed class GraphMailProvider : IMailProvider
{
    private const string ResourceType = "mail";

    private readonly GraphAuthProvider _authProvider;
    private readonly ISyncStateRepository _syncStateRepo;
    private readonly ILogger<GraphMailProvider> _logger;
    private GraphServiceClient? _client;

    public string AccountId { get; }

    public GraphMailProvider(
        string accountId,
        GraphAuthProvider authProvider,
        ISyncStateRepository syncStateRepo,
        ILogger<GraphMailProvider> logger)
    {
        AccountId = accountId;
        _authProvider = authProvider;
        _syncStateRepo = syncStateRepo;
        _logger = logger;
    }

    public async Task AuthenticateAsync(CancellationToken ct)
    {
        var token = await _authProvider.GetAccessTokenAsync(ct);
        _client = GraphClientFactory.Create(token);
        _logger.LogInformation("Graph Mail authenticated for account {AccountId}", AccountId);
    }

    public async Task<SyncResult<EmailHeader>> SyncMailAsync(DateTimeOffset since, CancellationToken ct)
    {
        EnsureClient();
        var (_, syncToken) = await _syncStateRepo.GetAsync(AccountId, ResourceType, ct);

        if (syncToken is not null)
            return await DeltaSyncAsync(syncToken, ct);

        return await FullSyncAsync(since, ct);
    }

    public async Task<string> FetchBodyAsync(string messageId, CancellationToken ct)
    {
        EnsureClient();

        var message = await _client!.Me.Messages[messageId]
            .GetAsync(config =>
            {
                config.QueryParameters.Select = ["body"];
                config.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
            }, ct);

        return message?.Body?.Content ?? "";
    }

    public async Task<string> DownloadAttachmentAsync(
        string messageId, string filename, string targetDir, CancellationToken ct)
    {
        EnsureClient();

        var attachments = await _client!.Me.Messages[messageId].Attachments
            .GetAsync(cancellationToken: ct);

        var fileAttachment = attachments?.Value?
            .OfType<FileAttachment>()
            .FirstOrDefault(a => string.Equals(a.Name, filename, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Attachment '{filename}' not found in message '{messageId}'.");

        Directory.CreateDirectory(targetDir);
        var filePath = Path.Combine(targetDir, filename);
        await File.WriteAllBytesAsync(filePath, fileAttachment.ContentBytes ?? [], ct);
        return filePath;
    }

    public async Task SendAsync(OutboundEmail email, CancellationToken ct)
    {
        EnsureClient();

        var request = GraphMailMapper.ToSendRequest(email);
        await _client!.Me.SendMail.PostAsync(request, cancellationToken: ct);

        _logger.LogInformation("Sent email to {To}, subject: {Subject}",
            string.Join(", ", email.To), email.Subject);
    }

    public async Task<List<EmailHeader>> RemoteSearchAsync(string query, CancellationToken ct)
    {
        EnsureClient();

        var searchRequest = new QueryPostRequestBody
        {
            Requests =
            [
                new Microsoft.Graph.Models.SearchRequest
                {
                    EntityTypes = [EntityType.Message],
                    Query = new SearchQuery { QueryString = query },
                    Size = 50,
                },
            ],
        };

        var response = await _client!.Search.Query.PostAsQueryPostResponseAsync(
            searchRequest, cancellationToken: ct);

        var results = new List<EmailHeader>();
        var hits = response?.Value?.FirstOrDefault()?.HitsContainers?.FirstOrDefault()?.Hits;
        if (hits is null)
            return results;

        foreach (var hit in hits)
        {
            if (hit.Resource is Message msg)
                results.Add(GraphMailMapper.ToEmailHeader(msg, AccountId));
        }

        return results;
    }

    public async Task SetFlagsAsync(string messageId, bool? isRead, bool? isFlagged, CancellationToken ct)
    {
        EnsureClient();

        var update = new Message();
        if (isRead.HasValue)
            update.IsRead = isRead.Value;
        if (isFlagged.HasValue)
            update.Flag = new FollowupFlag
            {
                FlagStatus = isFlagged.Value
                    ? FollowupFlagStatus.Flagged
                    : FollowupFlagStatus.NotFlagged,
            };

        await _client!.Me.Messages[messageId].PatchAsync(update, cancellationToken: ct);
    }

    private async Task<SyncResult<EmailHeader>> FullSyncAsync(DateTimeOffset since, CancellationToken ct)
    {
        var upserted = new List<EmailHeader>();
        string? deltaLink = null;

        var response = await _client!.Me.MailFolders["inbox"].Messages.Delta
            .GetAsDeltaGetResponseAsync(config =>
            {
                config.QueryParameters.Select =
                [
                    "id", "subject", "from", "toRecipients", "ccRecipients",
                    "receivedDateTime", "isRead", "flag", "bodyPreview",
                    "parentFolderId", "hasAttachments",
                ];
            }, ct);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                foreach (var msg in response.Value)
                {
                    if (msg.ReceivedDateTime >= since)
                        upserted.Add(GraphMailMapper.ToEmailHeader(msg, AccountId));
                }
            }

            // Check for deltaLink (final page) or nextLink (more pages)
            if (response.OdataNextLink is not null)
            {
                response = await _client.Me.MailFolders["inbox"].Messages.Delta
                    .WithUrl(response.OdataNextLink)
                    .GetAsDeltaGetResponseAsync(cancellationToken: ct);
            }
            else
            {
                deltaLink = response.OdataDeltaLink;
                break;
            }
        }

        if (deltaLink is not null)
        {
            await _syncStateRepo.SetAsync(AccountId, ResourceType,
                DateTimeOffset.UtcNow, deltaLink, ct);
        }

        return new SyncResult<EmailHeader>(upserted, [], deltaLink);
    }

    private async Task<SyncResult<EmailHeader>> DeltaSyncAsync(string deltaLink, CancellationToken ct)
    {
        var upserted = new List<EmailHeader>();
        var deletedIds = new List<string>();
        string? newDeltaLink = null;

        try
        {
            var response = await _client!.Me.MailFolders["inbox"].Messages.Delta
                .WithUrl(deltaLink)
                .GetAsDeltaGetResponseAsync(cancellationToken: ct);

            while (response is not null)
            {
                if (response.Value is not null)
                {
                    foreach (var msg in response.Value)
                    {
                        if (msg.AdditionalData?.ContainsKey("@removed") == true)
                        {
                            if (msg.Id is not null)
                                deletedIds.Add(msg.Id);
                        }
                        else
                        {
                            upserted.Add(GraphMailMapper.ToEmailHeader(msg, AccountId));
                        }
                    }
                }

                if (response.OdataNextLink is not null)
                {
                    response = await _client.Me.MailFolders["inbox"].Messages.Delta
                        .WithUrl(response.OdataNextLink)
                        .GetAsDeltaGetResponseAsync(cancellationToken: ct);
                }
                else
                {
                    newDeltaLink = response.OdataDeltaLink;
                    break;
                }
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 410)
        {
            // Delta token expired — fall back to full sync
            _logger.LogWarning("Delta token expired for mail sync, performing full sync");
            return await FullSyncAsync(DateTimeOffset.MinValue, ct);
        }

        if (newDeltaLink is not null)
        {
            await _syncStateRepo.SetAsync(AccountId, ResourceType,
                DateTimeOffset.UtcNow, newDeltaLink, ct);
        }

        return new SyncResult<EmailHeader>(upserted, deletedIds, newDeltaLink);
    }

    private void EnsureClient()
    {
        if (_client is null)
            throw new InvalidOperationException("Call AuthenticateAsync before using the provider.");
    }
}
