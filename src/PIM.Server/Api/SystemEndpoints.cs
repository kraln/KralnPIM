using PIM.Core.Config;
using PIM.Core.Providers;
using PIM.Server.Models;
using PIM.Server.Registration;
using PIM.Server.Services;

namespace PIM.Server.Api;

internal static class SystemEndpoints
{
    internal static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system");

        group.MapGet("/power", async (IPowerInfoProvider provider, CancellationToken ct) =>
        {
            var info = await provider.GetAsync(ct);
            return Results.Ok(info);
        });

        group.MapGet("/weather", async (IWeatherProvider provider, PimConfig config, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(config.System.WeatherLocation))
                return Results.Json(new ErrorResponse("Weather location not configured."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 400);

            var parts = config.System.WeatherLocation.Split(',');
            if (parts.Length != 2
                || !double.TryParse(parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture, out var lon))
            {
                return Results.Json(new ErrorResponse("Invalid weather location format. Expected 'lat,lon'."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 400);
            }

            var info = await provider.GetCurrentAsync(lat, lon, ct);
            return Results.Ok(info);
        });

        group.MapGet("/clock", (IClockProvider provider, PimConfig config) =>
        {
            var timezones = new List<string> { config.Ui.TimezonePrimary };
            if (!string.IsNullOrEmpty(config.Ui.TimezoneSecondary))
                timezones.Add(config.Ui.TimezoneSecondary);

            var info = provider.GetCurrent(timezones);
            return Results.Ok(info);
        });

        group.MapGet("/status", (ProviderRegistry registry, AccountStatusTracker tracker, PimConfig config) =>
        {
            var accounts = config.Accounts.Select(a => new AccountStatusInfo(
                a.Id,
                a.DisplayName,
                tracker.IsOnline(a.Id),
                null)).ToList();

            return Results.Ok(new SystemStatus(accounts));
        });
    }
}
