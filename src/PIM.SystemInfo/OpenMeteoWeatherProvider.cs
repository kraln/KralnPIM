using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.SystemInfo;

public sealed class OpenMeteoWeatherProvider : IWeatherProvider
{
    private static readonly WeatherInfo Fallback = new(0, "Unknown", 0, 0);

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenMeteoWeatherProvider> _logger;

    public OpenMeteoWeatherProvider(HttpClient httpClient, ILogger<OpenMeteoWeatherProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WeatherInfo> GetCurrentAsync(double lat, double lon, CancellationToken ct)
    {
        try
        {
            var url = string.Format(
                CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m",
                lat, lon);

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var current = doc.RootElement.GetProperty("current");

            var temperature = current.GetProperty("temperature_2m").GetDouble();
            var humidity = current.GetProperty("relative_humidity_2m").GetInt32();
            var weatherCode = current.GetProperty("weather_code").GetInt32();
            var windSpeed = current.GetProperty("wind_speed_10m").GetDouble();

            var condition = MapWeatherCode(weatherCode);

            return new WeatherInfo(temperature, condition, humidity, windSpeed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch weather data");
            return Fallback;
        }
    }

    internal static string MapWeatherCode(int code) => code switch
    {
        0 => "Clear",
        1 => "Mainly Clear",
        2 => "Partly Cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing Drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing Rain",
        71 or 73 or 75 => "Snow",
        77 => "Snow Grains",
        80 or 81 or 82 => "Rain Showers",
        85 or 86 => "Snow Showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with Hail",
        _ => "Unknown",
    };
}
