using System.Collections.Concurrent;

namespace PIM.Server.Services;

public sealed class AccountStatusTracker
{
    private readonly ConcurrentDictionary<string, bool> _status = new();

    public void MarkOnline(string accountId) => _status[accountId] = true;

    public void MarkOffline(string accountId) => _status[accountId] = false;

    public bool IsOnline(string accountId) =>
        _status.TryGetValue(accountId, out var online) && online;

    public IReadOnlyDictionary<string, bool> GetAll() =>
        _status.ToDictionary(kv => kv.Key, kv => kv.Value);
}
