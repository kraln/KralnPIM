namespace PIM.Core.Models;

public sealed record CalendarEvent(
    string EventId,
    string AccountId,
    string CalendarId,
    string Summary,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    List<string> Invitees,
    string? RecurrenceRule,
    EventStatus Status
);
