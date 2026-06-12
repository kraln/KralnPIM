using System.Text.Json.Serialization;

namespace PIM.Sync.EventKit;

internal sealed record EventKitCalendarListResponse(
    int SchemaVersion,
    EventKitCalendarListPayload? Data,
    EventKitErrorBody? Error
);

internal sealed record EventKitEventListResponse(
    int SchemaVersion,
    EventKitEventListPayload? Data,
    EventKitErrorBody? Error
);

internal sealed record EventKitErrorBody(string Code, string Message);

internal sealed record EventKitCalendarListPayload(List<EventKitCalendarDto> Calendars);

internal sealed record EventKitEventListPayload(List<EventKitEventDto> Events);

internal sealed record EventKitCalendarDto(
    string Id,
    string Title,
    string Source,
    string SourceType,
    bool AllowsModifications,
    bool IsSubscribed,
    string? Color
);

internal sealed record EventKitEventDto(
    string Id,
    string CalendarId,
    string Title,
    string Start,
    string End,
    bool IsAllDay,
    string? OccurrenceDate,
    bool IsDetached,
    string Status,
    string Availability,
    string? Location,
    string? Notes,
    string? Url,
    EventKitOrganizerDto? Organizer,
    List<EventKitParticipantDto> Attendees
);

internal sealed record EventKitOrganizerDto(
    string? Name,
    string? Email,
    bool IsCurrentUser
);

internal sealed record EventKitParticipantDto(
    string? Name,
    string? Email,
    string Status,
    string Role,
    bool IsCurrentUser
);

[JsonSerializable(typeof(EventKitCalendarListResponse))]
[JsonSerializable(typeof(EventKitEventListResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class EventKitJsonContext : JsonSerializerContext { }
