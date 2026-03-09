namespace PIM.Core.Models;

public sealed record WeatherInfo(
    double TemperatureCelsius,
    string Condition,
    int HumidityPercent,
    double WindSpeedKmh,
    List<DailyForecast> Daily = null!,
    string? LocationName = null
);

public sealed record DailyForecast(
    DateOnly Date,
    TimeOnly? Sunrise,
    TimeOnly? Sunset,
    string? Condition = null,
    double? HighCelsius = null,
    double? LowCelsius = null
);
