using System.Globalization;
using PIM.Core.Models;

namespace PIM.Core.Data;

public sealed class SqliteAuthRepository : IAuthRepository
{
    private readonly DbConnectionFactory _factory;

    public SqliteAuthRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task SaveOAuthTokenAsync(OAuthToken token, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO oauth_tokens (account_id, access_token, refresh_token, expires_at)
            VALUES (@aid, @access, @refresh, @expires)
            """;
        cmd.Parameters.AddWithValue("@aid", token.AccountId);
        cmd.Parameters.AddWithValue("@access", token.AccessToken);
        cmd.Parameters.AddWithValue("@refresh", token.RefreshToken);
        cmd.Parameters.AddWithValue("@expires", token.ExpiresAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<OAuthToken?> GetOAuthTokenAsync(string accountId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT account_id, access_token, refresh_token, expires_at FROM oauth_tokens WHERE account_id = @aid";
        cmd.Parameters.AddWithValue("@aid", accountId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new OAuthToken(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)
        );
    }

    public async Task SaveImapPasswordAsync(string accountId, string password, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO imap_credentials (account_id, password)
            VALUES (@aid, @pwd)
            """;
        cmd.Parameters.AddWithValue("@aid", accountId);
        cmd.Parameters.AddWithValue("@pwd", password);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetImapPasswordAsync(string accountId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password FROM imap_credentials WHERE account_id = @aid";
        cmd.Parameters.AddWithValue("@aid", accountId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    // CalDAV passwords share the imap_credentials table — account IDs are unique across types
    public Task SaveCalDavPasswordAsync(string accountId, string password, CancellationToken ct = default) =>
        SaveImapPasswordAsync(accountId, password, ct);

    public Task<string?> GetCalDavPasswordAsync(string accountId, CancellationToken ct = default) =>
        GetImapPasswordAsync(accountId, ct);

    public async Task DeletePasswordAsync(string accountId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM imap_credentials WHERE account_id = @aid";
        cmd.Parameters.AddWithValue("@aid", accountId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteOAuthTokenAsync(string accountId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM oauth_tokens WHERE account_id = @aid";
        cmd.Parameters.AddWithValue("@aid", accountId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
