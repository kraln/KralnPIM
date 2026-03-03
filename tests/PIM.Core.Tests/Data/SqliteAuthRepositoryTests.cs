using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Tests.TestHelpers;

namespace PIM.Core.Tests.Data;

public class SqliteAuthRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly SqliteAuthRepository _repo;

    public SqliteAuthRepositoryTests()
    {
        _repo = new SqliteAuthRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SaveAndGetOAuthToken_RoundTrip()
    {
        var token = new OAuthToken(
            AccountId: "acc-1",
            AccessToken: "access-123",
            RefreshToken: "refresh-456",
            ExpiresAt: new DateTimeOffset(2025, 2, 1, 12, 0, 0, TimeSpan.Zero)
        );

        await _repo.SaveOAuthTokenAsync(token);
        var result = await _repo.GetOAuthTokenAsync("acc-1");

        Assert.NotNull(result);
        Assert.Equal("acc-1", result.AccountId);
        Assert.Equal("access-123", result.AccessToken);
        Assert.Equal("refresh-456", result.RefreshToken);
        Assert.Equal(token.ExpiresAt, result.ExpiresAt);
    }

    [Fact]
    public async Task SaveOAuthToken_UpdatesExisting()
    {
        await _repo.SaveOAuthTokenAsync(new OAuthToken("acc-1", "old-access", "old-refresh",
            DateTimeOffset.UtcNow.AddHours(1)));
        await _repo.SaveOAuthTokenAsync(new OAuthToken("acc-1", "new-access", "new-refresh",
            DateTimeOffset.UtcNow.AddHours(2)));

        var result = await _repo.GetOAuthTokenAsync("acc-1");
        Assert.NotNull(result);
        Assert.Equal("new-access", result.AccessToken);
        Assert.Equal("new-refresh", result.RefreshToken);
    }

    [Fact]
    public async Task GetOAuthToken_NonExistent_ReturnsNull()
    {
        var result = await _repo.GetOAuthTokenAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndGetImapPassword_RoundTrip()
    {
        await _repo.SaveImapPasswordAsync("acc-1", "my-secret-password");
        var result = await _repo.GetImapPasswordAsync("acc-1");

        Assert.Equal("my-secret-password", result);
    }

    [Fact]
    public async Task SaveImapPassword_UpdatesExisting()
    {
        await _repo.SaveImapPasswordAsync("acc-1", "old-password");
        await _repo.SaveImapPasswordAsync("acc-1", "new-password");

        var result = await _repo.GetImapPasswordAsync("acc-1");
        Assert.Equal("new-password", result);
    }

    [Fact]
    public async Task GetImapPassword_NonExistent_ReturnsNull()
    {
        var result = await _repo.GetImapPasswordAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndGetCalDavPassword_RoundTrip()
    {
        await _repo.SaveCalDavPasswordAsync("caldav-1", "caldav-secret");
        var result = await _repo.GetCalDavPasswordAsync("caldav-1");

        Assert.Equal("caldav-secret", result);
    }

    [Fact]
    public async Task CalDavAndImapPasswords_SeparateByAccountId()
    {
        await _repo.SaveImapPasswordAsync("imap-acc", "imap-pass");
        await _repo.SaveCalDavPasswordAsync("caldav-acc", "caldav-pass");

        Assert.Equal("imap-pass", await _repo.GetImapPasswordAsync("imap-acc"));
        Assert.Equal("caldav-pass", await _repo.GetCalDavPasswordAsync("caldav-acc"));
        Assert.Null(await _repo.GetImapPasswordAsync("caldav-acc-other"));
    }

    [Fact]
    public async Task DeletePassword_RemovesCredential()
    {
        await _repo.SaveImapPasswordAsync("acc-1", "password");
        Assert.NotNull(await _repo.GetImapPasswordAsync("acc-1"));

        await _repo.DeletePasswordAsync("acc-1");
        Assert.Null(await _repo.GetImapPasswordAsync("acc-1"));
    }

    [Fact]
    public async Task DeleteOAuthToken_RemovesToken()
    {
        await _repo.SaveOAuthTokenAsync(new OAuthToken("acc-1", "access", "refresh",
            DateTimeOffset.UtcNow.AddHours(1)));
        Assert.NotNull(await _repo.GetOAuthTokenAsync("acc-1"));

        await _repo.DeleteOAuthTokenAsync("acc-1");
        Assert.Null(await _repo.GetOAuthTokenAsync("acc-1"));
    }
}
