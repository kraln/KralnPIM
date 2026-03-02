using PIM.Core.Models;

namespace PIM.Core.Providers;

public interface IWeatherProvider
{
    Task<WeatherInfo> GetCurrentAsync(double lat, double lon, CancellationToken ct);
}
