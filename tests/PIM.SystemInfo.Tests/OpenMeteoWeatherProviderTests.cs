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

    [Theory]
    [InlineData(0, "Clear")]
    [InlineData(1, "Mainly Clear")]
    [InlineData(2, "Partly Cloudy")]
    [InlineData(3, "Overcast")]
    [InlineData(45, "Fog")]
    [InlineData(48, "Fog")]
    [InlineData(51, "Drizzle")]
    [InlineData(61, "Rain")]
    [InlineData(66, "Freezing Rain")]
    [InlineData(71, "Snow")]
    [InlineData(77, "Snow Grains")]
    [InlineData(80, "Rain Showers")]
    [InlineData(85, "Snow Showers")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(96, "Thunderstorm with Hail")]
    [InlineData(99, "Thunderstorm with Hail")]
    [InlineData(999, "Unknown")]
    public void MapWeatherCode_AllCodes_MapCorrectly(int code, string expected)
    {
        Assert.Equal(expected, OpenMeteoWeatherProvider.MapWeatherCode(code));
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
