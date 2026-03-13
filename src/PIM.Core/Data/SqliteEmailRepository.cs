using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PIM.Core.Models;
using PIM.Core.Serialization;

namespace PIM.Core.Data;

public sealed class SqliteEmailRepository : IEmailRepository
{
    private readonly DbConnectionFactory _factory;

    public SqliteEmailRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task UpsertHeadersAsync(IEnumerable<EmailHeader> headers, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var transaction = conn.BeginTransaction();

        foreach (var header in headers)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT OR REPLACE INTO email_headers
                (message_id, account_id, folder_id, subject, from_address, from_display,
                 to_addresses, cc_addresses, date, is_read, is_flagged, snippet, attachments, synced_at)
                VALUES (@mid, @aid, @fid, @sub, @from, @fromDisp,
                        @to, @cc, @date, @read, @flag, @snip, @attach, @synced)
                """;
            cmd.Parameters.AddWithValue("@mid", header.MessageId);
            cmd.Parameters.AddWithValue("@aid", header.AccountId);
            cmd.Parameters.AddWithValue("@fid", header.FolderId);
            cmd.Parameters.AddWithValue("@sub", (object?)header.Subject ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@from", (object?)header.FromAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fromDisp", (object?)header.FromDisplayName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@to", JsonSerializer.Serialize(header.ToAddresses, PimJsonContext.Default.ListString));
            cmd.Parameters.AddWithValue("@cc", JsonSerializer.Serialize(header.CcAddresses, PimJsonContext.Default.ListString));
            cmd.Parameters.AddWithValue("@date", header.Date.ToUniversalTime().ToString("O"));
            cmd.Parameters.AddWithValue("@read", header.IsRead ? 1 : 0);
            cmd.Parameters.AddWithValue("@flag", header.IsFlagged ? 1 : 0);
            cmd.Parameters.AddWithValue("@snip", (object?)header.PlainTextSnippet ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@attach", JsonSerializer.Serialize(header.Attachments, PimJsonContext.Default.ListAttachmentInfo));
            cmd.Parameters.AddWithValue("@synced", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);
        }

        transaction.Commit();
    }

    public async Task UpsertBodyAsync(string messageId, string plainText, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO email_bodies (message_id, plain_text, synced_at)
            VALUES (@mid, @text, @synced)
            """;
        cmd.Parameters.AddWithValue("@mid", messageId);
        cmd.Parameters.AddWithValue("@text", plainText);
        cmd.Parameters.AddWithValue("@synced", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<EmailHeader?> GetHeaderAsync(string messageId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM email_headers WHERE message_id = @mid";
        cmd.Parameters.AddWithValue("@mid", messageId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadHeader(reader);
    }

    public async Task<EmailBody?> GetBodyAsync(string messageId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT message_id, plain_text FROM email_bodies WHERE message_id = @mid";
        cmd.Parameters.AddWithValue("@mid", messageId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new EmailBody(reader.GetString(0), reader.GetString(1));
    }

    public async Task<List<EmailHeader>> ListAsync(EmailListQuery query, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();

        var where = new List<string>();

        if (query.AccountId != null)
        {
            where.Add("account_id = @aid");
            cmd.Parameters.AddWithValue("@aid", query.AccountId);
        }

        if (query.IsRead != null)
        {
            where.Add("is_read = @read");
            cmd.Parameters.AddWithValue("@read", query.IsRead.Value ? 1 : 0);
        }

        if (query.IsFlagged != null)
        {
            where.Add("is_flagged = @flag");
            cmd.Parameters.AddWithValue("@flag", query.IsFlagged.Value ? 1 : 0);
        }

        var whereClause = where.Count > 0 ? $"WHERE {string.Join(" AND ", where)}" : "";
        cmd.CommandText = $"SELECT * FROM email_headers {whereClause} ORDER BY date DESC LIMIT @limit OFFSET @offset";
        cmd.Parameters.AddWithValue("@limit", query.Limit);
        cmd.Parameters.AddWithValue("@offset", query.Offset);

        var results = new List<EmailHeader>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadHeader(reader));

        return results;
    }

    public async Task SetReadAsync(string messageId, bool isRead, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE email_headers SET is_read = @val WHERE message_id = @mid";
        cmd.Parameters.AddWithValue("@val", isRead ? 1 : 0);
        cmd.Parameters.AddWithValue("@mid", messageId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetFlaggedAsync(string messageId, bool isFlagged, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE email_headers SET is_flagged = @val WHERE message_id = @mid";
        cmd.Parameters.AddWithValue("@val", isFlagged ? 1 : 0);
        cmd.Parameters.AddWithValue("@mid", messageId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string messageId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM email_headers WHERE message_id = @id";
        cmd.Parameters.AddWithValue("@id", messageId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM email_headers WHERE date < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<EmailHeader>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT eh.*
            FROM email_fts fts
            JOIN email_headers eh ON eh.rowid = fts.rowid
            WHERE email_fts MATCH @query
            ORDER BY rank
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@query", query);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<EmailHeader>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadHeader(reader));
        return results;
    }

    public async Task<Dictionary<string, (int Unread, int Flagged)>> GetAccountCountsAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT account_id,
                   SUM(CASE WHEN is_read = 0 THEN 1 ELSE 0 END) AS unread,
                   SUM(CASE WHEN is_flagged = 1 THEN 1 ELSE 0 END) AS flagged
            FROM email_headers
            GROUP BY account_id
            """;

        var results = new Dictionary<string, (int Unread, int Flagged)>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var accountId = reader.GetString(0);
            var unread = reader.GetInt32(1);
            var flagged = reader.GetInt32(2);
            results[accountId] = (unread, flagged);
        }
        return results;
    }

    private static EmailHeader ReadHeader(SqliteDataReader reader)
    {
        return new EmailHeader(
            MessageId: reader.GetString(reader.GetOrdinal("message_id")),
            AccountId: reader.GetString(reader.GetOrdinal("account_id")),
            FolderId: reader.GetString(reader.GetOrdinal("folder_id")),
            Subject: reader.IsDBNull(reader.GetOrdinal("subject")) ? "" : reader.GetString(reader.GetOrdinal("subject")),
            FromAddress: reader.IsDBNull(reader.GetOrdinal("from_address")) ? "" : reader.GetString(reader.GetOrdinal("from_address")),
            FromDisplayName: reader.IsDBNull(reader.GetOrdinal("from_display")) ? "" : reader.GetString(reader.GetOrdinal("from_display")),
            ToAddresses: DeserializeList(reader, "to_addresses"),
            CcAddresses: DeserializeList(reader, "cc_addresses"),
            Date: DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("date")), CultureInfo.InvariantCulture),
            IsRead: reader.GetInt32(reader.GetOrdinal("is_read")) != 0,
            IsFlagged: reader.GetInt32(reader.GetOrdinal("is_flagged")) != 0,
            PlainTextSnippet: reader.IsDBNull(reader.GetOrdinal("snippet")) ? null : reader.GetString(reader.GetOrdinal("snippet")),
            Attachments: DeserializeAttachments(reader, "attachments")
        );
    }

    private static List<string> DeserializeList(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return [];
        var json = reader.GetString(ordinal);
        return JsonSerializer.Deserialize(json, PimJsonContext.Default.ListString) ?? [];
    }

    private static List<AttachmentInfo> DeserializeAttachments(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return [];
        var json = reader.GetString(ordinal);
        return JsonSerializer.Deserialize(json, PimJsonContext.Default.ListAttachmentInfo) ?? [];
    }
}
