namespace PIM.Core.Models;

public sealed record EmailListQuery(
    string? AccountId = null,
    bool? IsRead = null,
    bool? IsFlagged = null,
    int Offset = 0,
    int Limit = 50
);
