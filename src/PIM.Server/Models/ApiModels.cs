using PIM.Core.Models;

namespace PIM.Server.Models;

public sealed record MailFlagPatch(bool? IsRead, bool? IsFlagged);

public sealed record MailDetail(EmailHeader Header, string? PlainTextBody);

public sealed record AccountOverview(
    string Id,
    string DisplayName,
    string Type,
    bool Online,
    int UnreadCount,
    int FlaggedCount);

public sealed record DeepSearchRequest(string Query, string? Scope);

public sealed record ErrorResponse(string Error);

public sealed record SystemStatus(List<AccountStatusInfo> Accounts);

public sealed record AccountStatusInfo(
    string AccountId,
    string DisplayName,
    bool Online,
    DateTimeOffset? LastSync);
