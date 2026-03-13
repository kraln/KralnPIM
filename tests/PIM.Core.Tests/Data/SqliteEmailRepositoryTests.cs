using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Tests.TestHelpers;

namespace PIM.Core.Tests.Data;

public class SqliteEmailRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly SqliteEmailRepository _repo;

    public SqliteEmailRepositoryTests()
    {
        _repo = new SqliteEmailRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private static EmailHeader MakeHeader(
        string messageId = "msg-001",
        string accountId = "acc-1",
        string subject = "Test Subject",
        DateTimeOffset? date = null,
        bool isRead = false,
        bool isFlagged = false,
        string? snippet = "A snippet",
        List<AttachmentInfo>? attachments = null)
    {
        return new EmailHeader(
            MessageId: messageId,
            AccountId: accountId,
            FolderId: "INBOX",
            Subject: subject,
            FromAddress: "alice@example.com",
            FromDisplayName: "Alice",
            ToAddresses: ["bob@example.com"],
            CcAddresses: ["carol@example.com"],
            Date: date ?? new DateTimeOffset(2025, 1, 6, 10, 0, 0, TimeSpan.Zero),
            IsRead: isRead,
            IsFlagged: isFlagged,
            PlainTextSnippet: snippet,
            Attachments: attachments ?? []
        );
    }

    [Fact]
    public async Task UpsertHeaders_SingleHeader_CanRetrieve()
    {
        var header = MakeHeader();
        await _repo.UpsertHeadersAsync([header]);

        var result = await _repo.GetHeaderAsync("msg-001");
        Assert.NotNull(result);
        Assert.Equal("msg-001", result.MessageId);
        Assert.Equal("Test Subject", result.Subject);
        Assert.Equal("alice@example.com", result.FromAddress);
        Assert.Equal(["bob@example.com"], result.ToAddresses);
        Assert.Equal(["carol@example.com"], result.CcAddresses);
    }

    [Fact]
    public async Task UpsertHeaders_MultipleHeaders_AllRetrievable()
    {
        var h1 = MakeHeader("msg-1", subject: "First");
        var h2 = MakeHeader("msg-2", subject: "Second");
        await _repo.UpsertHeadersAsync([h1, h2]);

        Assert.NotNull(await _repo.GetHeaderAsync("msg-1"));
        Assert.NotNull(await _repo.GetHeaderAsync("msg-2"));
    }

    [Fact]
    public async Task UpsertHeaders_DuplicateMessageId_Updates()
    {
        await _repo.UpsertHeadersAsync([MakeHeader(subject: "Original")]);
        await _repo.UpsertHeadersAsync([MakeHeader(subject: "Updated")]);

        var result = await _repo.GetHeaderAsync("msg-001");
        Assert.NotNull(result);
        Assert.Equal("Updated", result.Subject);
    }

    [Fact]
    public async Task UpsertBody_CanRetrieve()
    {
        await _repo.UpsertHeadersAsync([MakeHeader()]);
        await _repo.UpsertBodyAsync("msg-001", "Full body text here.");

        var body = await _repo.GetBodyAsync("msg-001");
        Assert.NotNull(body);
        Assert.Equal("msg-001", body.MessageId);
        Assert.Equal("Full body text here.", body.PlainTextContent);
    }

    [Fact]
    public async Task GetHeader_NonExistent_ReturnsNull()
    {
        var result = await _repo.GetHeaderAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBody_NonExistent_ReturnsNull()
    {
        var result = await _repo.GetBodyAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_NoFilters_ReturnsLatestFirst()
    {
        var h1 = MakeHeader("msg-1", date: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var h2 = MakeHeader("msg-2", date: new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero));
        var h3 = MakeHeader("msg-3", date: new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero));
        await _repo.UpsertHeadersAsync([h1, h2, h3]);

        var results = await _repo.ListAsync(new EmailListQuery());
        Assert.Equal(3, results.Count);
        Assert.Equal("msg-2", results[0].MessageId); // latest first
        Assert.Equal("msg-3", results[1].MessageId);
        Assert.Equal("msg-1", results[2].MessageId);
    }

    [Fact]
    public async Task ListAsync_FilterByAccountId()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-1", accountId: "acc-1"),
            MakeHeader("msg-2", accountId: "acc-2"),
            MakeHeader("msg-3", accountId: "acc-1")
        ]);

        var results = await _repo.ListAsync(new EmailListQuery(AccountId: "acc-1"));
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("acc-1", r.AccountId));
    }

    [Fact]
    public async Task ListAsync_FilterByIsRead()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-1", isRead: true),
            MakeHeader("msg-2", isRead: false),
            MakeHeader("msg-3", isRead: false)
        ]);

        var unread = await _repo.ListAsync(new EmailListQuery(IsRead: false));
        Assert.Equal(2, unread.Count);

        var read = await _repo.ListAsync(new EmailListQuery(IsRead: true));
        Assert.Single(read);
    }

    [Fact]
    public async Task ListAsync_FilterByIsFlagged()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-1", isFlagged: true),
            MakeHeader("msg-2", isFlagged: false)
        ]);

        var flagged = await _repo.ListAsync(new EmailListQuery(IsFlagged: true));
        Assert.Single(flagged);
        Assert.Equal("msg-1", flagged[0].MessageId);
    }

    [Fact]
    public async Task ListAsync_Pagination()
    {
        for (int i = 0; i < 10; i++)
        {
            await _repo.UpsertHeadersAsync([MakeHeader($"msg-{i:D2}",
                date: new DateTimeOffset(2025, 1, 1 + i, 0, 0, 0, TimeSpan.Zero))]);
        }

        var page1 = await _repo.ListAsync(new EmailListQuery(Limit: 3, Offset: 0));
        var page2 = await _repo.ListAsync(new EmailListQuery(Limit: 3, Offset: 3));

        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.NotEqual(page1[0].MessageId, page2[0].MessageId);
    }

    [Fact]
    public async Task ListAsync_EmptyDatabase_ReturnsEmptyList()
    {
        var results = await _repo.ListAsync(new EmailListQuery());
        Assert.Empty(results);
    }

    [Fact]
    public async Task SetRead_UpdatesFlag()
    {
        await _repo.UpsertHeadersAsync([MakeHeader(isRead: false)]);
        await _repo.SetReadAsync("msg-001", true);

        var result = await _repo.GetHeaderAsync("msg-001");
        Assert.NotNull(result);
        Assert.True(result.IsRead);
    }

    [Fact]
    public async Task SetFlagged_UpdatesFlag()
    {
        await _repo.UpsertHeadersAsync([MakeHeader(isFlagged: false)]);
        await _repo.SetFlaggedAsync("msg-001", true);

        var result = await _repo.GetHeaderAsync("msg-001");
        Assert.NotNull(result);
        Assert.True(result.IsFlagged);
    }

    [Fact]
    public async Task PurgeOlderThan_RemovesOldHeaders()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-old", date: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            MakeHeader("msg-new", date: new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero))
        ]);

        await _repo.PurgeOlderThanAsync(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Null(await _repo.GetHeaderAsync("msg-old"));
        Assert.NotNull(await _repo.GetHeaderAsync("msg-new"));
    }

    [Fact]
    public async Task PurgeOlderThan_CascadesToBodies()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-old", date: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero))
        ]);
        await _repo.UpsertBodyAsync("msg-old", "Old body text");

        await _repo.PurgeOlderThanAsync(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Null(await _repo.GetBodyAsync("msg-old"));
    }

    [Fact]
    public async Task GetAccountCounts_EmptyDatabase_ReturnsEmptyDictionary()
    {
        var counts = await _repo.GetAccountCountsAsync();
        Assert.Empty(counts);
    }

    [Fact]
    public async Task GetAccountCounts_SingleAccount_CountsCorrectly()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-1", accountId: "acc-1", isRead: false, isFlagged: false),
            MakeHeader("msg-2", accountId: "acc-1", isRead: false, isFlagged: true),
            MakeHeader("msg-3", accountId: "acc-1", isRead: true, isFlagged: true),
            MakeHeader("msg-4", accountId: "acc-1", isRead: true, isFlagged: false),
        ]);

        var counts = await _repo.GetAccountCountsAsync();
        Assert.Single(counts);
        Assert.Equal(2, counts["acc-1"].Unread);
        Assert.Equal(2, counts["acc-1"].Flagged);
    }

    [Fact]
    public async Task GetAccountCounts_MultipleAccounts_GroupsCorrectly()
    {
        await _repo.UpsertHeadersAsync([
            MakeHeader("msg-1", accountId: "acc-1", isRead: false, isFlagged: false),
            MakeHeader("msg-2", accountId: "acc-1", isRead: false, isFlagged: true),
            MakeHeader("msg-3", accountId: "acc-2", isRead: true, isFlagged: false),
            MakeHeader("msg-4", accountId: "acc-2", isRead: false, isFlagged: true),
        ]);

        var counts = await _repo.GetAccountCountsAsync();
        Assert.Equal(2, counts.Count);
        Assert.Equal(2, counts["acc-1"].Unread);
        Assert.Equal(1, counts["acc-1"].Flagged);
        Assert.Equal(1, counts["acc-2"].Unread);
        Assert.Equal(1, counts["acc-2"].Flagged);
    }

    [Fact]
    public async Task JsonFields_Attachments_RoundTrip()
    {
        var attachments = new List<AttachmentInfo>
        {
            new("doc.pdf", "application/pdf", 1024),
            new("image.png", "image/png", 2048)
        };
        await _repo.UpsertHeadersAsync([MakeHeader(attachments: attachments)]);

        var result = await _repo.GetHeaderAsync("msg-001");
        Assert.NotNull(result);
        Assert.Equal(2, result.Attachments.Count);
        Assert.Equal("doc.pdf", result.Attachments[0].Filename);
        Assert.Equal(2048, result.Attachments[1].SizeBytes);
    }
}
