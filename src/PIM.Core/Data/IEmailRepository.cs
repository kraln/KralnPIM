using PIM.Core.Models;

namespace PIM.Core.Data;

public interface IEmailRepository
{
    Task UpsertHeadersAsync(IEnumerable<EmailHeader> headers, CancellationToken ct = default);
    Task UpsertBodyAsync(string messageId, string plainText, CancellationToken ct = default);
    Task<EmailHeader?> GetHeaderAsync(string messageId, CancellationToken ct = default);
    Task<EmailBody?> GetBodyAsync(string messageId, CancellationToken ct = default);
    Task<List<EmailHeader>> ListAsync(EmailListQuery query, CancellationToken ct = default);
    Task SetReadAsync(string messageId, bool isRead, CancellationToken ct = default);
    Task SetFlaggedAsync(string messageId, bool isFlagged, CancellationToken ct = default);
    Task DeleteAsync(string messageId, CancellationToken ct = default);
    Task PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
    Task<List<EmailHeader>> SearchAsync(string query, int limit, CancellationToken ct = default);
}
