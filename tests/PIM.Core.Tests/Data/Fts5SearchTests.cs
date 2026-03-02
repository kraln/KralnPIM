using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Tests.TestHelpers;

namespace PIM.Core.Tests.Data;

public class Fts5SearchTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly SqliteEmailRepository _repo;

    public Fts5SearchTests()
    {
        _repo = new SqliteEmailRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private static EmailHeader MakeHeader(
        string messageId, string subject, string fromAddress = "alice@example.com",
        string fromDisplay = "Alice", string? snippet = null)
    {
        return new EmailHeader(
            MessageId: messageId,
            AccountId: "acc-1",
            FolderId: "INBOX",
            Subject: subject,
            FromAddress: fromAddress,
            FromDisplayName: fromDisplay,
            ToAddresses: ["bob@example.com"],
            CcAddresses: [],
            Date: new DateTimeOffset(2025, 1, 6, 10, 0, 0, TimeSpan.Zero),
            IsRead: false,
            IsFlagged: false,
            PlainTextSnippet: snippet,
            Attachments: []
        );
    }

    private async Task<List<string>> SearchFts(string query)
    {
        using var conn = _db.Factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT eh.message_id
            FROM email_fts fts
            JOIN email_headers eh ON eh.rowid = fts.rowid
            WHERE email_fts MATCH @query
            ORDER BY rank
            """;
        cmd.Parameters.AddWithValue("@query", query);

        var results = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(reader.GetString(0));
        return results;
    }

    [Fact]
    public async Task Search_BySubject_FindsMatches()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-1", "Project Alpha Update"),
            MakeHeader("msg-2", "Meeting Notes for Beta"),
            MakeHeader("msg-3", "Alpha Release Schedule")
        ]);

        var results = await SearchFts("Alpha");
        Assert.Equal(2, results.Count);
        Assert.Contains("msg-1", results);
        Assert.Contains("msg-3", results);
    }

    [Fact]
    public async Task Search_ByFromAddress_FindsMatches()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-1", "Subject A", fromAddress: "bob@special.com"),
            MakeHeader("msg-2", "Subject B", fromAddress: "alice@normal.com")
        ]);

        var results = await SearchFts("special");
        Assert.Single(results);
        Assert.Equal("msg-1", results[0]);
    }

    [Fact]
    public async Task Search_ByBodyText_FindsMatches()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-1", "Regular Subject"),
            MakeHeader("msg-2", "Another Subject")
        ]);
        await _repo.UpsertBodyAsync("msg-1", "This email discusses the quarterly revenue report in detail.");
        await _repo.UpsertBodyAsync("msg-2", "Just a quick hello to check in.");

        var results = await SearchFts("quarterly revenue");
        Assert.Single(results);
        Assert.Equal("msg-1", results[0]);
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsEmpty()
    {
        await _repo.UpsertHeadersAsync([MakeHeader("msg-1", "Hello World")]);

        var results = await SearchFts("zzzznonexistent");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_BySnippet_FindsMatches()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-1", "Test", snippet: "Discussion about kubernetes deployment"),
            MakeHeader("msg-2", "Test", snippet: "Lunch plans for Friday")
        ]);

        var results = await SearchFts("kubernetes");
        Assert.Single(results);
        Assert.Equal("msg-1", results[0]);
    }
}
