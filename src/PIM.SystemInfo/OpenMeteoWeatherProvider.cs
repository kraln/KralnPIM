using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.SystemInfo;

public sealed class OpenMeteoWeatherProvider : IWeatherProvider
{
    private static readonly WeatherInfo Fallback = new(0, "Unknown", 0, 0, []);

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenMeteoWeatherProvider> _logger;
    private string? _cachedLocationName;
    private bool _locationLookedUp;

    public OpenMeteoWeatherProvider(HttpClient httpClient, ILogger<OpenMeteoWeatherProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WeatherInfo> GetCurrentAsync(double lat, double lon, CancellationToken ct)
    {
        try
        {
            // One-time reverse geocode via Nominatim
            if (!_locationLookedUp)
            {
                _locationLookedUp = true;
                _cachedLocationName = await ReverseGeocodeAsync(lat, lon, ct);
            }

            var url = string.Format(
                CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m&daily=sunrise,sunset,weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=7",
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

            var dailyForecasts = new List<DailyForecast>();
            if (doc.RootElement.TryGetProperty("daily", out var daily))
            {
                var dates = daily.TryGetProperty("time", out var timeArr) ? timeArr : default;
                var sunrises = daily.TryGetProperty("sunrise", out var srArr) ? srArr : default;
                var sunsets = daily.TryGetProperty("sunset", out var ssArr) ? ssArr : default;
                var codes = daily.TryGetProperty("weather_code", out var wcArr) ? wcArr : default;
                var highs = daily.TryGetProperty("temperature_2m_max", out var hiArr) ? hiArr : default;
                var lows = daily.TryGetProperty("temperature_2m_min", out var loArr) ? loArr : default;

                var count = dates.ValueKind == JsonValueKind.Array ? dates.GetArrayLength() : 0;
                for (var i = 0; i < count; i++)
                {
                    DateOnly date = default;
                    if (dates[i].GetString() is { } ds && DateOnly.TryParse(ds, out var d))
                        date = d;

                    TimeOnly? sunrise = null;
                    if (sunrises.ValueKind == JsonValueKind.Array && i < sunrises.GetArrayLength()
                        && sunrises[i].GetString() is { } srs && DateTime.TryParse(srs, out var srDt))
                        sunrise = TimeOnly.FromDateTime(srDt);

                    TimeOnly? sunset = null;
                    if (sunsets.ValueKind == JsonValueKind.Array && i < sunsets.GetArrayLength()
                        && sunsets[i].GetString() is { } sss && DateTime.TryParse(sss, out var ssDt))
                        sunset = TimeOnly.FromDateTime(ssDt);

                    string? dayCondition = null;
                    if (codes.ValueKind == JsonValueKind.Array && i < codes.GetArrayLength())
                        dayCondition = MapWeatherCode(codes[i].GetInt32());

                    double? high = null;
                    if (highs.ValueKind == JsonValueKind.Array && i < highs.GetArrayLength())
                        high = highs[i].GetDouble();

                    double? low = null;
                    if (lows.ValueKind == JsonValueKind.Array && i < lows.GetArrayLength())
                        low = lows[i].GetDouble();

                    dailyForecasts.Add(new DailyForecast(date, sunrise, sunset, dayCondition, high, low));
                }
            }

            return new WeatherInfo(temperature, condition, humidity, windSpeed, dailyForecasts, _cachedLocationName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch weather data");
            return Fallback;
        }
    }

    private async Task<string?> ReverseGeocodeAsync(double lat, double lon, CancellationToken ct)
    {
        try
        {
            var url = string.Format(
                CultureInfo.InvariantCulture,
                "https://nominatim.openstreetmap.org/reverse?lat={0}&lon={1}&format=json&zoom=10",
                lat, lon);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("KralnPIM/1.0");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            // Try city/town/village from address, fall back to display_name
            if (doc.RootElement.TryGetProperty("address", out var addr))
            {
                foreach (var key in new[] { "city", "town", "village", "municipality" })
                {
                    if (addr.TryGetProperty(key, out var val) && val.GetString() is { } name)
                        return name;
                }
            }

            if (doc.RootElement.TryGetProperty("name", out var nameProp)
                && nameProp.GetString() is { Length: > 0 } n)
                return n;

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Nominatim reverse geocode failed");
            return null;
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
