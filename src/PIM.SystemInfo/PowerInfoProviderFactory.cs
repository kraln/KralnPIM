using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PIM.Core.Providers;

namespace PIM.SystemInfo;

public static class PowerInfoProviderFactory
{
    public static IPowerInfoProvider Create(ILoggerFactory loggerFactory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxPowerInfoProvider(loggerFactory.CreateLogger<LinuxPowerInfoProvider>());

        return new FallbackPowerInfoProvider();
    }
}
