namespace PIM.Core.Models;

public sealed record PowerInfo(
    int BatteryPercent,
    string? TimeRemaining,
    double? DrainWatts
);
