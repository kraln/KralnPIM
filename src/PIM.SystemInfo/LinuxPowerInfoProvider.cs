using System.Globalization;
using Microsoft.Extensions.Logging;
using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.SystemInfo;

public sealed class LinuxPowerInfoProvider : IPowerInfoProvider
{
    private const string BatteryBasePath = "/sys/class/power_supply/BAT0";
    private static readonly PowerInfo NoBattery = new(-1, null, null);

    private readonly ILogger<LinuxPowerInfoProvider> _logger;

    public LinuxPowerInfoProvider(ILogger<LinuxPowerInfoProvider> logger)
    {
        _logger = logger;
    }

    // Overridable for testing
    internal string BasePath { get; init; } = BatteryBasePath;

    public Task<PowerInfo> GetAsync(CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(BasePath))
            {
                _logger.LogDebug("Battery path {Path} not found, no battery detected", BasePath);
                return Task.FromResult(NoBattery);
            }

            var percent = ReadInt(Path.Combine(BasePath, "capacity")) ?? -1;
            if (percent == -1)
                return Task.FromResult(NoBattery);

            var drainWatts = ReadDrainWatts();
            var timeRemaining = EstimateTimeRemaining(drainWatts);

            return Task.FromResult(new PowerInfo(percent, timeRemaining, drainWatts));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read battery info");
            return Task.FromResult(NoBattery);
        }
    }

    private double? ReadDrainWatts()
    {
        // Try power_now first (in microwatts)
        var powerNow = ReadLong(Path.Combine(BasePath, "power_now"));
        if (powerNow is > 0)
            return powerNow.Value / 1_000_000.0;

        // Fall back to voltage_now * current_now
        var voltageNow = ReadLong(Path.Combine(BasePath, "voltage_now"));
        var currentNow = ReadLong(Path.Combine(BasePath, "current_now"));
        if (voltageNow is > 0 && currentNow is > 0)
            return voltageNow.Value * currentNow.Value / 1_000_000_000_000.0; // µV * µA → W

        return null;
    }

    private string? EstimateTimeRemaining(double? drainWatts)
    {
        if (drainWatts is null or <= 0)
            return null;

        var status = ReadString(Path.Combine(BasePath, "status"));
        if (status is not "Discharging")
            return null;

        var energyNow = ReadLong(Path.Combine(BasePath, "energy_now"));
        if (energyNow is null or <= 0)
            return null;

        var energyWh = energyNow.Value / 1_000_000.0;
        var hoursRemaining = energyWh / drainWatts.Value;

        var hours = (int)hoursRemaining;
        var minutes = (int)((hoursRemaining - hours) * 60);
        return $"{hours}h {minutes:D2}m";
    }

    private static int? ReadInt(string path)
    {
        var text = ReadString(path);
        return text is not null && int.TryParse(text, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static long? ReadLong(string path)
    {
        var text = ReadString(path);
        return text is not null && long.TryParse(text, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? ReadString(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
