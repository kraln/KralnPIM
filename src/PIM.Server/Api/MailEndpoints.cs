using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;
using PIM.Server.Models;
using PIM.Server.Registration;
using PIM.Server.Services;

namespace PIM.Server.Api;

internal static class MailEndpoints
{
    internal static void MapMailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/mail");

        group.MapGet("/", async (
            IEmailRepository repo,
            string? accountId,
            bool? isRead,
            bool? isFlagged,
            int offset = 0,
            int limit = 50,
            CancellationToken ct = default) =>
        {
            var query = new EmailListQuery(accountId, isRead, isFlagged, offset, limit);
            var headers = await repo.ListAsync(query, ct);
            return Results.Ok(headers);
        });

        group.MapGet("/accounts", (
            PimConfig config,
            AccountStatusTracker tracker) =>
        {
            var accounts = config.Accounts.Select(a => new AccountOverview(
                a.Id,
                a.DisplayName,
                a.Type.ToString(),
                tracker.IsOnline(a.Id),
                0, 0)).ToList();
            return Results.Ok(accounts);
        });

        group.MapGet("/{messageId}", async (
            string messageId,
            IEmailRepository repo,
            ProviderRegistry registry,
            AccountStatusTracker tracker,
            PimConfig config,
            CancellationToken ct) =>
        {
            var header = await repo.GetHeaderAsync(messageId, ct);
            if (header is null)
                return Results.Json(new ErrorResponse("Message not found."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 404);

            var body = await repo.GetBodyAsync(messageId, ct);

            // On-demand fetch if body not cached
            if (body is null && tracker.IsOnline(header.AccountId))
            {
                var provider = registry.GetMailProvider(header.AccountId);
                if (provider is not null)
                {
                    var plainText = await provider.FetchBodyAsync(messageId, ct);
                    await repo.UpsertBodyAsync(messageId, plainText, ct);
                    body = new EmailBody(messageId, plainText);
                }
            }

            return Results.Ok(new MailDetail(header, body?.PlainTextContent));
        });

        group.MapPatch("/{messageId}", async (
            string messageId,
            MailFlagPatch patch,
            IEmailRepository repo,
            ProviderRegistry registry,
            AccountStatusTracker tracker,
            CancellationToken ct) =>
        {
            var header = await repo.GetHeaderAsync(messageId, ct);
            if (header is null)
                return Results.Json(new ErrorResponse("Message not found."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 404);

            if (patch.IsRead is not null)
                await repo.SetReadAsync(messageId, patch.IsRead.Value, ct);
            if (patch.IsFlagged is not null)
                await repo.SetFlaggedAsync(messageId, patch.IsFlagged.Value, ct);

            // Sync flags to remote if online
            if (tracker.IsOnline(header.AccountId))
            {
                var provider = registry.GetMailProvider(header.AccountId);
                if (provider is not null)
                    await provider.SetFlagsAsync(messageId, patch.IsRead, patch.IsFlagged, ct);
            }

            return Results.Ok();
        });

        group.MapPost("/send", async (
            OutboundEmail email,
            ProviderRegistry registry,
            AccountStatusTracker tracker,
            CancellationToken ct) =>
        {
            if (!tracker.IsOnline(email.FromAccountId))
                return Results.Json(new ErrorResponse($"Account '{email.FromAccountId}' is offline."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 503);

            var provider = registry.GetMailProvider(email.FromAccountId);
            if (provider is null)
                return Results.Json(new ErrorResponse($"Account '{email.FromAccountId}' not found."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 404);

            await provider.SendAsync(email, ct);
            return Results.Accepted();
        });

        group.MapGet("/attachment/{messageId}/{filename}", async (
            string messageId,
            string filename,
            IEmailRepository repo,
            ProviderRegistry registry,
            AccountStatusTracker tracker,
            PimConfig config,
            CancellationToken ct) =>
        {
            var header = await repo.GetHeaderAsync(messageId, ct);
            if (header is null)
                return Results.Json(new ErrorResponse("Message not found."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 404);

            if (!tracker.IsOnline(header.AccountId))
                return Results.Json(new ErrorResponse($"Account '{header.AccountId}' is offline."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 503);

            var provider = registry.GetMailProvider(header.AccountId);
            if (provider is null)
                return Results.Json(new ErrorResponse("Provider not found."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 404);

            var filePath = await provider.DownloadAttachmentAsync(
                messageId, filename, config.Storage.AttachmentDownloadDir, ct);
            return Results.Ok(new { filePath });
        });
    }
}
