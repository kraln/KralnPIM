using PIM.Core.Models;

namespace PIM.Core.Data;

public interface IAuthRepository
{
    Task SaveOAuthTokenAsync(OAuthToken token, CancellationToken ct = default);
    Task<OAuthToken?> GetOAuthTokenAsync(string accountId, CancellationToken ct = default);
    Task SaveImapPasswordAsync(string accountId, string password, CancellationToken ct = default);
    Task<string?> GetImapPasswordAsync(string accountId, CancellationToken ct = default);
    Task SaveCalDavPasswordAsync(string accountId, string password, CancellationToken ct = default);
    Task<string?> GetCalDavPasswordAsync(string accountId, CancellationToken ct = default);
    Task DeletePasswordAsync(string accountId, CancellationToken ct = default);
    Task DeleteOAuthTokenAsync(string accountId, CancellationToken ct = default);
}
