using System.Collections.Concurrent;

namespace PIM.Server.Services;

public enum OfflineReason
{
    None,
    AuthRequired,
    Error,
}

public sealed class AccountStatusTracker
{
    private readonly ConcurrentDictionary<string, (bool Online, OfflineReason Reason)> _status = new();

    public void MarkOnline(string accountId) =>
        _status[accountId] = (true, OfflineReason.None);

    public void MarkOffline(string accountId, OfflineReason reason = OfflineReason.Error) =>
        _status[accountId] = (false, reason);

    public bool IsOnline(string accountId) =>
        !_status.TryGetValue(accountId, out var entry) || entry.Online;

    public OfflineReason GetOfflineReason(string accountId) =>
        _status.TryGetValue(accountId, out var entry) ? entry.Reason : OfflineReason.None;

    public IReadOnlyDictionary<string, bool> GetAll() =>
        _status.ToDictionary(kv => kv.Key, kv => kv.Value.Online);
}
