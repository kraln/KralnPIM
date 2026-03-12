namespace PIM.Server.WebSocket;

public record WsEvent(string Type, string AccountId);

public sealed record MailSyncEvent(
    string AccountId,
    int NewCount,
    List<string> UpdatedIds) : WsEvent("mail.sync", AccountId);

public sealed record CalendarSyncEvent(
    string AccountId,
    int UpdatedCount) : WsEvent("calendar.sync", AccountId);

public sealed record StatusChangeEvent(
    string AccountId,
    bool Online,
    string? Reason = null) : WsEvent("status.change", AccountId);
