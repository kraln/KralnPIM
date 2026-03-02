using System.Globalization;

namespace PIM.Core.Data;

public sealed class SqliteSyncStateRepository : ISyncStateRepository
{
    private readonly DbConnectionFactory _factory;

    public SqliteSyncStateRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<(DateTimeOffset? LastSync, string? SyncToken)> GetAsync(
        string accountId, string resourceType, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_sync, sync_token FROM sync_state WHERE account_id = @aid AND resource_type = @rt";
        cmd.Parameters.AddWithValue("@aid", accountId);
        cmd.Parameters.AddWithValue("@rt", resourceType);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return (null, null);

        DateTimeOffset? lastSync = reader.IsDBNull(0)
            ? null
            : DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        string? syncToken = reader.IsDBNull(1) ? null : reader.GetString(1);

        return (lastSync, syncToken);
    }

    public async Task SetAsync(
        string accountId, string resourceType, DateTimeOffset lastSync, string? syncToken,
        CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO sync_state (account_id, resource_type, last_sync, sync_token)
            VALUES (@aid, @rt, @ls, @st)
            """;
        cmd.Parameters.AddWithValue("@aid", accountId);
        cmd.Parameters.AddWithValue("@rt", resourceType);
        cmd.Parameters.AddWithValue("@ls", lastSync.ToString("O"));
        cmd.Parameters.AddWithValue("@st", (object?)syncToken ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
