using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Server.Registration;
using PIM.Server.Services;
using PIM.Server.WebSocket;

namespace PIM.Server.Sync;

public sealed class SyncScheduler : BackgroundService
{
    private readonly ProviderRegistry _registry;
    private readonly IEmailRepository _emailRepo;
    private readonly ICalendarRepository _calendarRepo;
    private readonly AccountStatusTracker _statusTracker;
    private readonly WebSocketBroadcaster _broadcaster;
    private readonly StorageConfig _storageConfig;
    private readonly FreeBusySinkService _freeBusySink;
    private readonly ILogger<SyncScheduler> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeSpan _interval;

    public SyncScheduler(
        ProviderRegistry registry,
        IEmailRepository emailRepo,
        ICalendarRepository calendarRepo,
        AccountStatusTracker statusTracker,
        WebSocketBroadcaster broadcaster,
        PimConfig config,
        FreeBusySinkService freeBusySink,
        ILogger<SyncScheduler> logger,
        ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _emailRepo = emailRepo;
        _calendarRepo = calendarRepo;
        _statusTracker = statusTracker;
        _broadcaster = broadcaster;
        _storageConfig = config.Storage;
        _freeBusySink = freeBusySink;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _interval = TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sync scheduler started, interval: {Interval}", _interval);

        // Run initial sync immediately
        await RunSyncCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunSyncCycleAsync(stoppingToken);
        }
    }

    private async Task RunSyncCycleAsync(CancellationToken ct)
    {
        _logger.LogDebug("Starting sync cycle");

        var worker = new AccountSyncWorker(
            _registry, _emailRepo, _calendarRepo,
            _statusTracker, _broadcaster, _storageConfig,
            _freeBusySink,
            _loggerFactory.CreateLogger<AccountSyncWorker>());

        foreach (var accountId in _registry.AccountIds)
        {
            try
            {
                await worker.SyncAsync(accountId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error syncing account {AccountId}", accountId);
            }
        }

        _logger.LogDebug("Sync cycle completed");
    }
}
