using Microsoft.Extensions.Logging;
using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.SystemInfo;

public sealed class ClockProvider : IClockProvider
{
    private readonly ILogger<ClockProvider> _logger;

    public ClockProvider(ILogger<ClockProvider> logger)
    {
        _logger = logger;
    }

    public ClockInfo GetCurrent(List<string> timezoneIds)
    {
        var now = DateTimeOffset.UtcNow;
        var zones = new List<TimeZoneDisplay>();

        foreach (var id in timezoneIds)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(id);
                var converted = TimeZoneInfo.ConvertTime(now, tz);
                var label = tz.IsDaylightSavingTime(converted) ? tz.DaylightName : tz.StandardName;

                zones.Add(new TimeZoneDisplay(id, label, converted));
            }
            catch (TimeZoneNotFoundException)
            {
                _logger.LogWarning("Unknown timezone ID: {TimezoneId}", id);
            }
            catch (InvalidTimeZoneException)
            {
                _logger.LogWarning("Invalid timezone ID: {TimezoneId}", id);
            }
        }

        return new ClockInfo(zones);
    }
}
