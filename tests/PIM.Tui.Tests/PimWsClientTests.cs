using Microsoft.Extensions.Logging.Abstractions;
using PIM.Tui.Client;
using PIM.Tui.Models;

namespace PIM.Tui.Tests;

public sealed class PimWsClientTests : IAsyncDisposable
{
    private readonly PimWsClient _client;

    public PimWsClientTests()
    {
        _client = new PimWsClient(
            new Uri("ws://localhost:9401/ws"),
            NullLogger<PimWsClient>.Instance);
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();

    [Fact]
    public void DispatchEvent_MailSync_FiresOnMailSync()
    {
        MailSyncEvent? received = null;
        _client.OnMailSync += evt => received = evt;

        _client.DispatchEvent("""{"type":"mail.sync","accountId":"work","newCount":3,"updatedIds":["msg-1","msg-2"]}""");

        Assert.NotNull(received);
        Assert.Equal("mail.sync", received.Type);
        Assert.Equal("work", received.AccountId);
        Assert.Equal(3, received.NewCount);
        Assert.Equal(2, received.UpdatedIds.Count);
    }

    [Fact]
    public void DispatchEvent_CalendarSync_FiresOnCalendarSync()
    {
        CalendarSyncEvent? received = null;
        _client.OnCalendarSync += evt => received = evt;

        _client.DispatchEvent("""{"type":"calendar.sync","accountId":"personal","updatedCount":5}""");

        Assert.NotNull(received);
        Assert.Equal("calendar.sync", received.Type);
        Assert.Equal("personal", received.AccountId);
        Assert.Equal(5, received.UpdatedCount);
    }

    [Fact]
    public void DispatchEvent_StatusChange_FiresOnStatusChange()
    {
        StatusChangeEvent? received = null;
        _client.OnStatusChange += evt => received = evt;

        _client.DispatchEvent("""{"type":"status.change","accountId":"imap","online":false}""");

        Assert.NotNull(received);
        Assert.Equal("status.change", received.Type);
        Assert.Equal("imap", received.AccountId);
        Assert.False(received.Online);
    }

    [Fact]
    public void DispatchEvent_UnknownType_DoesNotThrow()
    {
        _client.DispatchEvent("""{"type":"unknown.event","accountId":"x"}""");
    }

    [Fact]
    public void DispatchEvent_InvalidJson_DoesNotThrow()
    {
        _client.DispatchEvent("not json at all");
    }
}
