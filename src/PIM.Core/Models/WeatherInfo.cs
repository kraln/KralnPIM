namespace PIM.Core.Models;

public sealed record WeatherInfo(
    double TemperatureCelsius,
    string Condition,
    int HumidityPercent,
    double WindSpeedKmh
);
