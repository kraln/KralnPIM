using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Core.Providers;
using PIM.Server.Registration;
using PIM.Server.Services;
using PIM.Server.WebSocket;

namespace PIM.Server.Sync;

public sealed class AccountSyncWorker
{
    private readonly ProviderRegistry _registry;
    private readonly IEmailRepository _emailRepo;
    private readonly ICalendarRepository _calendarRepo;
    private readonly AccountStatusTracker _statusTracker;
    private readonly WebSocketBroadcaster _broadcaster;
    private readonly StorageConfig _storageConfig;
    private readonly ILogger<AccountSyncWorker> _logger;

    private const int MaxRetries = 3;

    public AccountSyncWorker(
        ProviderRegistry registry,
        IEmailRepository emailRepo,
        ICalendarRepository calendarRepo,
        AccountStatusTracker statusTracker,
        WebSocketBroadcaster broadcaster,
        StorageConfig storageConfig,
        ILogger<AccountSyncWorker> logger)
    {
        _registry = registry;
        _emailRepo = emailRepo;
        _calendarRepo = calendarRepo;
        _statusTracker = statusTracker;
        _broadcaster = broadcaster;
        _storageConfig = storageConfig;
        _logger = logger;
    }

    public async Task SyncAsync(string accountId, CancellationToken ct)
    {
        await SyncMailAsync(accountId, ct);
        await SyncCalendarsAsync(accountId, ct);
        await PurgeOldDataAsync(ct);
    }

    private async Task SyncMailAsync(string accountId, CancellationToken ct)
    {
        var provider = _registry.GetMailProvider(accountId);
        if (provider is null)
            return;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var since = DateTimeOffset.UtcNow.AddMonths(-_storageConfig.BufferMonthsBack);
                var result = await provider.SyncMailAsync(since, ct);

                if (result.Upserted.Count > 0)
                    await _emailRepo.UpsertHeadersAsync(result.Upserted, ct);

                _statusTracker.MarkOnline(accountId);

                await _broadcaster.BroadcastAsync(
                    new MailSyncEvent(accountId, result.Upserted.Count, result.Upserted.Select(h => h.MessageId).ToList()),
                    ct);

                if (result.Upserted.Count > 0 || result.DeletedIds.Count > 0)
                    _logger.LogInformation("Mail sync for {AccountId}: {Upserted} upserted, {Deleted} deleted",
                        accountId, result.Upserted.Count, result.DeletedIds.Count);
                else
                    _logger.LogDebug("Mail sync for {AccountId}: no changes", accountId);
                return;
            }
            catch (Exception ex) when (IsAuthFailure(ex))
            {
                _logger.LogError(ex, "Auth failure for mail sync on {AccountId}, marking offline", accountId);
                _statusTracker.MarkOffline(accountId);
                await _broadcaster.BroadcastAsync(new StatusChangeEvent(accountId, false), ct);
                return;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(4, attempt - 1)); // 1s, 4s, 16s
                _logger.LogWarning(ex, "Mail sync attempt {Attempt}/{Max} failed for {AccountId}, retrying in {Delay}s",
                    attempt, MaxRetries, accountId, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mail sync failed after {Max} attempts for {AccountId}, marking offline",
                    MaxRetries, accountId);
                _statusTracker.MarkOffline(accountId);
                await _broadcaster.BroadcastAsync(new StatusChangeEvent(accountId, false), ct);
            }
        }
    }

    private async Task SyncCalendarsAsync(string accountId, CancellationToken ct)
    {
        var providers = _registry.GetCalendarProviders(accountId);
        if (providers.Count == 0)
            return;

        var rangeStart = DateTimeOffset.UtcNow.AddMonths(-_storageConfig.BufferMonthsBack);
        var rangeEnd = DateTimeOffset.UtcNow.AddMonths(_storageConfig.BufferMonthsForward);

        foreach (var provider in providers)
        {
            for (var attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var result = await provider.SyncEventsAsync(rangeStart, rangeEnd, ct);

                    if (result.Upserted.Count > 0)
                        await _calendarRepo.UpsertEventsAsync(result.Upserted, ct);

                    await _broadcaster.BroadcastAsync(
                        new CalendarSyncEvent(accountId, result.Upserted.Count), ct);

                    if (result.Upserted.Count > 0 || result.DeletedIds.Count > 0)
                        _logger.LogInformation("Calendar sync for {AccountId}: {Upserted} upserted, {Deleted} deleted",
                            accountId, result.Upserted.Count, result.DeletedIds.Count);
                    else
                        _logger.LogDebug("Calendar sync for {AccountId}: no changes", accountId);
                    break;
                }
                catch (Exception ex) when (IsAuthFailure(ex))
                {
                    _logger.LogError(ex, "Auth failure for calendar sync on {AccountId}", accountId);
                    break;
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(4, attempt - 1));
                    _logger.LogWarning(ex, "Calendar sync attempt {Attempt}/{Max} failed for {AccountId}, retrying in {Delay}s",
                        attempt, MaxRetries, accountId, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Calendar sync failed after {Max} attempts for {AccountId}",
                        MaxRetries, accountId);
                }
            }
        }
    }

    private async Task PurgeOldDataAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMonths(-_storageConfig.BufferMonthsBack);
        await _emailRepo.PurgeOlderThanAsync(cutoff, ct);
        await _calendarRepo.PurgeOlderThanAsync(cutoff, ct);
    }

    private static bool IsAuthFailure(Exception ex)
    {
        // Heuristic: auth failures typically surface as InvalidOperationException or
        // contain "auth", "unauthorized", "401" in the message
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("unauthorized") || msg.Contains("401") || msg.Contains("auth");
    }
}
