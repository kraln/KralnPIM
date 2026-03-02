namespace PIM.Core.Models;

public sealed record ClockInfo(
    List<TimeZoneDisplay> Zones
);

public sealed record TimeZoneDisplay(
    string TimezoneId,
    string Label,
    DateTimeOffset CurrentTime
);
