namespace PIM.Core.Data;

public interface ISyncStateRepository
{
    Task<(DateTimeOffset? LastSync, string? SyncToken)> GetAsync(string accountId, string resourceType, CancellationToken ct = default);
    Task SetAsync(string accountId, string resourceType, DateTimeOffset lastSync, string? syncToken, CancellationToken ct = default);
}
