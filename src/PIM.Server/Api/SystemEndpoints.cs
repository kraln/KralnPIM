using PIM.Core.Config;
using PIM.Core.Models;
using PIM.Core.Providers;
using PIM.Server.Models;
using PIM.Server.Registration;
using PIM.Server.Services;
using PIM.Server.WebSocket;

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
            if (config.System.WeatherLocationName is { } name)
                info = info with { LocationName = name };
            return Results.Ok(info);
        });

        group.MapGet("/clock", (IClockProvider provider, PimConfig config) =>
        {
            var timezones = new List<string> { config.Ui.TimezonePrimary };
            if (!string.IsNullOrEmpty(config.Ui.TimezoneSecondary))
                timezones.Add(config.Ui.TimezoneSecondary);

            var info = provider.GetCurrent(timezones);

            // Use the config IDs as labels — the user typed short friendly names
            // (e.g. "CET", "EST") but TimeZoneInfo.StandardName may return offsets.
            var zones = info.Zones.Select((z, i) => z with { Label = timezones[i] }).ToList();
            info = new ClockInfo(zones);

            return Results.Ok(info);
        });

        group.MapGet("/status", (ProviderRegistry registry, AccountStatusTracker tracker, PimConfig config) =>
        {
            var accounts = config.Accounts.Select(a =>
            {
                var online = tracker.IsOnline(a.Id);
                var reason = online ? null : tracker.GetOfflineReason(a.Id) switch
                {
                    Services.OfflineReason.AuthRequired => "auth_required",
                    Services.OfflineReason.Error => "error",
                    _ => null,
                };
                return new AccountStatusInfo(a.Id, a.DisplayName, online, reason, null);
            }).ToList();

            return Results.Ok(new SystemStatus(accounts));
        });

        group.MapPost("/reauth/{accountId}", async (
            string accountId,
            ProviderRegistry registry,
            AccountStatusTracker tracker,
            WebSocketBroadcaster broadcaster,
            CancellationToken ct) =>
        {
            if (tracker.IsOnline(accountId))
                return Results.Ok(new ReauthResponse(null, "Account is already online."));

            if (tracker.GetOfflineReason(accountId) != Services.OfflineReason.AuthRequired)
                return Results.Json(new ErrorResponse("Account is offline but does not require re-authorization."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 400);

            var authUrl = await registry.StartReauthAsync(accountId, tracker, broadcaster, ct);
            if (authUrl is null)
                return Results.Json(new ErrorResponse("Re-authorization not supported for this account type."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 400);

            return Results.Ok(new ReauthResponse(authUrl, "Open this URL in your browser to re-authorize."));
        });
    }
}
