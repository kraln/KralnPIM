using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;
using PIM.Server.Registration;
using PIM.Server.Services;
using PIM.Server.Sync;
using PIM.Server.WebSocket;

namespace PIM.Server.Tests;

public class AccountSyncWorkerTests
{
    private readonly IMailProvider _mailProvider;
    private readonly ICalendarProvider _calendarProvider;
    private readonly IEmailRepository _emailRepo;
    private readonly ICalendarRepository _calendarRepo;
    private readonly AccountStatusTracker _statusTracker;
    private readonly WebSocketBroadcaster _broadcaster;
    private readonly ProviderRegistry _registry;
    private readonly StorageConfig _storageConfig;

    public AccountSyncWorkerTests()
    {
        _mailProvider = Substitute.For<IMailProvider>();
        _mailProvider.AccountId.Returns("acc-1");
        _calendarProvider = Substitute.For<ICalendarProvider>();
        _calendarProvider.AccountId.Returns("acc-1");

        _emailRepo = Substitute.For<IEmailRepository>();
        _calendarRepo = Substitute.For<ICalendarRepository>();
        _statusTracker = new AccountStatusTracker();
        _broadcaster = new WebSocketBroadcaster(NullLogger<WebSocketBroadcaster>.Instance);
        _registry = CreateRegistryWithProviders();
        _storageConfig = new StorageConfig("test.db", "/tmp/attach", 6, 6);
    }

    private ProviderRegistry CreateRegistryWithProviders()
    {
        var registry = Substitute.For<ProviderRegistry>(NullLogger<ProviderRegistry>.Instance);

        // Use NSubstitute to return our mock providers
        registry.GetMailProvider("acc-1").Returns(_mailProvider);
        registry.GetCalendarProviders("acc-1").Returns(new List<ICalendarProvider> { _calendarProvider });
        registry.AccountIds.Returns(new[] { "acc-1" });
        return registry;
    }

    private AccountSyncWorker CreateWorker()
    {
        return new AccountSyncWorker(
            _registry, _emailRepo, _calendarRepo,
            _statusTracker, _broadcaster, _storageConfig,
            NullLogger<AccountSyncWorker>.Instance);
    }

    [Fact]
    public async Task Sync_SuccessfulMailSync_UpsertsAndMarksOnline()
    {
        var headers = new List<EmailHeader>
        {
            new("msg-1", "acc-1", "INBOX", "Test", "a@b.com", "A",
                ["b@c.com"], [], DateTimeOffset.UtcNow, false, false, null, [])
        };
        _mailProvider.SyncMailAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SyncResult<EmailHeader>(headers, [], null));
        _calendarProvider.SyncEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SyncResult<CalendarEvent>([], [], null));

        var worker = CreateWorker();
        await worker.SyncAsync("acc-1", CancellationToken.None);

        await _emailRepo.Received(1).UpsertHeadersAsync(
            Arg.Is<IEnumerable<EmailHeader>>(h => h.Count() == 1), Arg.Any<CancellationToken>());
        Assert.True(_statusTracker.IsOnline("acc-1"));
    }

    [Fact]
    public async Task Sync_AuthFailure_MarksOfflineImmediately()
    {
        _mailProvider.SyncMailAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Unauthorized: token expired"));
        _calendarProvider.SyncEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SyncResult<CalendarEvent>([], [], null));

        var worker = CreateWorker();
        await worker.SyncAsync("acc-1", CancellationToken.None);

        Assert.False(_statusTracker.IsOnline("acc-1"));
        // Should only be called once (no retries for auth failures)
        await _mailProvider.Received(1).SyncMailAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sync_EmptyResults_DoesNotCallUpsert()
    {
        _mailProvider.SyncMailAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SyncResult<EmailHeader>([], [], null));
        _calendarProvider.SyncEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SyncResult<CalendarEvent>([], [], null));

        var worker = CreateWorker();
        await worker.SyncAsync("acc-1", CancellationToken.None);

        await _emailRepo.DidNotReceive().UpsertHeadersAsync(
            Arg.Any<IEnumerable<EmailHeader>>(), Arg.Any<CancellationToken>());
    }
}
