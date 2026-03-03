namespace PIM.Core.Models;

public sealed record SearchResult(
    List<EmailHeader> Emails,
    List<CalendarEvent> Events
);
