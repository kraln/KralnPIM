namespace PIM.Tui.Models;

public sealed record WsEventEnvelope(string Type, string AccountId);

public sealed record MailSyncEvent(
    string Type,
    string AccountId,
    int NewCount,
    List<string> UpdatedIds);

public sealed record CalendarSyncEvent(
    string Type,
    string AccountId,
    int UpdatedCount);

public sealed record StatusChangeEvent(
    string Type,
    string AccountId,
    bool Online);
