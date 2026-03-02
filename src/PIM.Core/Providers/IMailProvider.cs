using PIM.Core.Models;

namespace PIM.Core.Providers;

public interface IMailProvider
{
    string AccountId { get; }
    Task AuthenticateAsync(CancellationToken ct);
    Task<SyncResult<EmailHeader>> SyncMailAsync(DateTimeOffset since, CancellationToken ct);
    Task<string> FetchBodyAsync(string messageId, CancellationToken ct);
    Task<string> DownloadAttachmentAsync(string messageId, string filename, string targetDir, CancellationToken ct);
    Task SendAsync(OutboundEmail email, CancellationToken ct);
    Task<List<EmailHeader>> RemoteSearchAsync(string query, CancellationToken ct);
    Task SetFlagsAsync(string messageId, bool? isRead, bool? isFlagged, CancellationToken ct);
}
