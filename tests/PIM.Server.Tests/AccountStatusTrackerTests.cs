using PIM.Server.Services;

namespace PIM.Server.Tests;

public class AccountStatusTrackerTests
{
    private readonly AccountStatusTracker _tracker = new();

    [Fact]
    public void UnknownAccount_DefaultsToOnline()
    {
        Assert.True(_tracker.IsOnline("nonexistent"));
    }

    [Fact]
    public void MarkOnline_MakesAccountOnline()
    {
        _tracker.MarkOnline("acc-1");
        Assert.True(_tracker.IsOnline("acc-1"));
    }

    [Fact]
    public void MarkOffline_MakesAccountOffline()
    {
        _tracker.MarkOnline("acc-1");
        _tracker.MarkOffline("acc-1");
        Assert.False(_tracker.IsOnline("acc-1"));
    }

    [Fact]
    public void GetAll_ReturnsAllTrackedAccounts()
    {
        _tracker.MarkOnline("acc-1");
        _tracker.MarkOffline("acc-2");

        var all = _tracker.GetAll();
        Assert.Equal(2, all.Count);
        Assert.True(all["acc-1"]);
        Assert.False(all["acc-2"]);
    }

    [Fact]
    public void MultipleUpdates_LastWins()
    {
        _tracker.MarkOnline("acc-1");
        _tracker.MarkOffline("acc-1");
        _tracker.MarkOnline("acc-1");
        Assert.True(_tracker.IsOnline("acc-1"));
    }
}
