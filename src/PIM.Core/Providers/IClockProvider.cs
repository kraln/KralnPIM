using PIM.Core.Models;

namespace PIM.Core.Providers;

public interface IClockProvider
{
    ClockInfo GetCurrent(List<string> timezoneIds);
}
