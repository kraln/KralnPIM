using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PIM.SystemInfo;

namespace PIM.SystemInfo.Tests;

public class OpenMeteoWeatherProviderTests
{
    private const string ValidResponse = """
        {
            "current": {
                "temperature_2m": 22.5,
                "relative_humidity_2m": 65,
                "weather_code": 0,
                "wind_speed_10m": 12.3
            }
        }
        """;

    [Fact]
    public async Task GetCurrentAsync_ValidResponse_ParsesCorrectly()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ValidResponse);
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(40.71, -74.01, CancellationToken.None);

        Assert.Equal(22.5, result.TemperatureCelsius);
        Assert.Equal("Clear", result.Condition);
        Assert.Equal(65, result.HumidityPercent);
        Assert.Equal(12.3, result.WindSpeedKmh);
    }

    [Fact]
    public async Task GetCurrentAsync_RainWeatherCode_MapsToRain()
    {
        var response = """
            {
                "current": {
                    "temperature_2m": 15.0,
                    "relative_humidity_2m": 90,
                    "weather_code": 61,
                    "wind_speed_10m": 20.0
                }
            }
            """;
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(51.5, -0.12, CancellationToken.None);

        Assert.Equal("Rain", result.Condition);
    }

    [Fact]
    public async Task GetCurrentAsync_HttpError_ReturnsFallback()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(0, 0, CancellationToken.None);

        Assert.Equal(0, result.TemperatureCelsius);
        Assert.Equal("Unknown", result.Condition);
        Assert.Equal(0, result.HumidityPercent);
        Assert.Equal(0, result.WindSpeedKmh);
    }

    [Fact]
    public async Task GetCurrentAsync_InvalidJson_ReturnsFallback()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "not json");
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(0, 0, CancellationToken.None);

        Assert.Equal("Unknown", result.Condition);
    }

    [Fact]
    public async Task GetCurrentAsync_Timeout_ReturnsFallback()
    {
        var handler = new TimeoutHttpMessageHandler();
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(0, 0, CancellationToken.None);

        Assert.Equal("Unknown", result.Condition);
    }

    [Fact]
    public async Task GetCurrentAsync_WithDailyBlock_ParsesDailyForecasts()
    {
        var response = """
            {
                "current": {
                    "temperature_2m": 20.0,
                    "relative_humidity_2m": 50,
                    "weather_code": 0,
                    "wind_speed_10m": 5.0
                },
                "daily": {
                    "time": ["2026-03-13", "2026-03-14"],
                    "sunrise": ["2026-03-13T06:30", "2026-03-14T06:29"],
                    "sunset": ["2026-03-13T18:15", "2026-03-14T18:16"],
                    "weather_code": [0, 61],
                    "temperature_2m_max": [22.0, 18.5],
                    "temperature_2m_min": [12.0, 10.0]
                }
            }
            """;
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(40.71, -74.01, CancellationToken.None);

        Assert.Equal(2, result.Daily.Count);
        Assert.Equal(new DateOnly(2026, 3, 13), result.Daily[0].Date);
        Assert.Equal(new TimeOnly(6, 30), result.Daily[0].Sunrise);
        Assert.Equal(new TimeOnly(18, 15), result.Daily[0].Sunset);
        Assert.Equal("Clear", result.Daily[0].Condition);
        Assert.Equal(22.0, result.Daily[0].HighCelsius);
        Assert.Equal(12.0, result.Daily[0].LowCelsius);
        Assert.Equal("Rain", result.Daily[1].Condition);
    }

    [Fact]
    public async Task GetCurrentAsync_DailyWithMissingSunrise_HandlesGracefully()
    {
        var response = """
            {
                "current": {
                    "temperature_2m": 20.0,
                    "relative_humidity_2m": 50,
                    "weather_code": 0,
                    "wind_speed_10m": 5.0
                },
                "daily": {
                    "time": ["2026-03-13"],
                    "weather_code": [2],
                    "temperature_2m_max": [22.0],
                    "temperature_2m_min": [12.0]
                }
            }
            """;
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(40.71, -74.01, CancellationToken.None);

        Assert.Single(result.Daily);
        Assert.Null(result.Daily[0].Sunrise);
        Assert.Null(result.Daily[0].Sunset);
        Assert.Equal("Partly Cloudy", result.Daily[0].Condition);
    }

    [Fact]
    public async Task GetCurrentAsync_NoDailyKey_ReturnsEmptyDailyList()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ValidResponse);
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(40.71, -74.01, CancellationToken.None);

        Assert.NotNull(result.Daily);
        Assert.Empty(result.Daily);
    }

    [Fact]
    public async Task GetCurrentAsync_ReverseGeocode_ReturnsCityName()
    {
        var handler = new RoutingHttpMessageHandler(new Dictionary<string, string>
        {
            ["nominatim"] = """{"address":{"city":"New York","country":"US"},"name":"NYC"}""",
            ["open-meteo"] = ValidResponse,
        });
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(40.71, -74.01, CancellationToken.None);

        Assert.Equal("New York", result.LocationName);
    }

    [Fact]
    public async Task GetCurrentAsync_ReverseGeocode_VillageOnly_ReturnsVillage()
    {
        var handler = new RoutingHttpMessageHandler(new Dictionary<string, string>
        {
            ["nominatim"] = """{"address":{"village":"Smalltown","country":"US"}}""",
            ["open-meteo"] = ValidResponse,
        });
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(40.71, -74.01, CancellationToken.None);

        Assert.Equal("Smalltown", result.LocationName);
    }

    [Fact]
    public async Task GetCurrentAsync_ReverseGeocode_NoAddressFields_FallsBackToName()
    {
        var handler = new RoutingHttpMessageHandler(new Dictionary<string, string>
        {
            ["nominatim"] = """{"address":{"country":"US"},"name":"Some Place"}""",
            ["open-meteo"] = ValidResponse,
        });
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        var result = await provider.GetCurrentAsync(40.71, -74.01, CancellationToken.None);

        Assert.Equal("Some Place", result.LocationName);
    }

    [Fact]
    public async Task GetCurrentAsync_ReverseGeocode_CalledOnlyOnce()
    {
        var handler = new RoutingHttpMessageHandler(new Dictionary<string, string>
        {
            ["nominatim"] = """{"address":{"city":"Cached City"}}""",
            ["open-meteo"] = ValidResponse,
        });
        var client = new HttpClient(handler);
        var provider = new OpenMeteoWeatherProvider(client, NullLogger<OpenMeteoWeatherProvider>.Instance);

        await provider.GetCurrentAsync(40.71, -74.01, CancellationToken.None);
        await provider.GetCurrentAsync(40.71, -74.01, CancellationToken.None);

        Assert.Equal(1, handler.CallCounts.GetValueOrDefault("nominatim", 0));
        Assert.Equal(2, handler.CallCounts.GetValueOrDefault("open-meteo", 0));
    }

    [Theory]
    [InlineData(0, "Clear")]
    [InlineData(1, "Mainly Clear")]
    [InlineData(2, "Partly Cloudy")]
    [InlineData(3, "Overcast")]
    [InlineData(45, "Fog")]
    [InlineData(48, "Fog")]
    [InlineData(51, "Drizzle")]
    [InlineData(53, "Drizzle")]
    [InlineData(55, "Drizzle")]
    [InlineData(56, "Freezing Drizzle")]
    [InlineData(57, "Freezing Drizzle")]
    [InlineData(61, "Rain")]
    [InlineData(63, "Rain")]
    [InlineData(65, "Rain")]
    [InlineData(66, "Freezing Rain")]
    [InlineData(67, "Freezing Rain")]
    [InlineData(71, "Snow")]
    [InlineData(73, "Snow")]
    [InlineData(75, "Snow")]
    [InlineData(77, "Snow Grains")]
    [InlineData(80, "Rain Showers")]
    [InlineData(81, "Rain Showers")]
    [InlineData(82, "Rain Showers")]
    [InlineData(85, "Snow Showers")]
    [InlineData(86, "Snow Showers")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(96, "Thunderstorm with Hail")]
    [InlineData(99, "Thunderstorm with Hail")]
    [InlineData(999, "Unknown")]
    public void MapWeatherCode_AllCodes_MapCorrectly(int code, string expected)
    {
        Assert.Equal(expected, OpenMeteoWeatherProvider.MapWeatherCode(code));
    }

    private sealed class RoutingHttpMessageHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        public Dictionary<string, int> CallCounts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.Host ?? "";
            var key = host.Contains("nominatim") ? "nominatim" : "open-meteo";
            CallCounts[key] = CallCounts.GetValueOrDefault(key, 0) + 1;

            var content = responses.GetValueOrDefault(key, "{}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class TimeoutHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new TaskCanceledException("Request timed out");
        }
    }
}
