using PIM.Core.Data;
using PIM.Core.Tests.TestHelpers;

namespace PIM.Core.Tests.Data;

public class SqliteSyncStateRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly SqliteSyncStateRepository _repo;

    public SqliteSyncStateRepositoryTests()
    {
        _repo = new SqliteSyncStateRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SetAndGet_RoundTrip()
    {
        var lastSync = new DateTimeOffset(2025, 1, 6, 12, 0, 0, TimeSpan.Zero);
        await _repo.SetAsync("acc-1", "email", lastSync, "token-abc");

        var (resultSync, resultToken) = await _repo.GetAsync("acc-1", "email");
        Assert.NotNull(resultSync);
        Assert.Equal(lastSync, resultSync.Value);
        Assert.Equal("token-abc", resultToken);
    }

    [Fact]
    public async Task Set_UpdatesExisting()
    {
        var time1 = new DateTimeOffset(2025, 1, 6, 12, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2025, 1, 6, 13, 0, 0, TimeSpan.Zero);

        await _repo.SetAsync("acc-1", "email", time1, "token-1");
        await _repo.SetAsync("acc-1", "email", time2, "token-2");

        var (resultSync, resultToken) = await _repo.GetAsync("acc-1", "email");
        Assert.Equal(time2, resultSync);
        Assert.Equal("token-2", resultToken);
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNulls()
    {
        var (lastSync, syncToken) = await _repo.GetAsync("nonexistent", "email");
        Assert.Null(lastSync);
        Assert.Null(syncToken);
    }

    [Fact]
    public async Task DifferentResourceTypes_Independent()
    {
        var time1 = new DateTimeOffset(2025, 1, 6, 12, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2025, 1, 6, 13, 0, 0, TimeSpan.Zero);

        await _repo.SetAsync("acc-1", "email", time1, "email-token");
        await _repo.SetAsync("acc-1", "calendar", time2, "cal-token");

        var (emailSync, emailToken) = await _repo.GetAsync("acc-1", "email");
        var (calSync, calToken) = await _repo.GetAsync("acc-1", "calendar");

        Assert.Equal(time1, emailSync);
        Assert.Equal("email-token", emailToken);
        Assert.Equal(time2, calSync);
        Assert.Equal("cal-token", calToken);
    }

    [Fact]
    public async Task Set_NullSyncToken_Allowed()
    {
        var time = new DateTimeOffset(2025, 1, 6, 12, 0, 0, TimeSpan.Zero);
        await _repo.SetAsync("acc-1", "email", time, null);

        var (resultSync, resultToken) = await _repo.GetAsync("acc-1", "email");
        Assert.Equal(time, resultSync);
        Assert.Null(resultToken);
    }
}
