namespace PIM.Core.Models;

public sealed record SyncResult<T>(
    List<T> Upserted,
    List<string> DeletedIds,
    string? NewSyncToken
);
