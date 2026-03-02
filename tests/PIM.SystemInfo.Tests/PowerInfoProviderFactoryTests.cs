using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PIM.SystemInfo;

namespace PIM.SystemInfo.Tests;

public class PowerInfoProviderFactoryTests
{
    [Fact]
    public void Create_ReturnsNonNullProvider()
    {
        var factory = NullLoggerFactory.Instance;
        var provider = PowerInfoProviderFactory.Create(factory);

        Assert.NotNull(provider);
    }

    [Fact]
    public void Create_OnLinux_ReturnsLinuxProvider()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return; // Skip on non-Linux

        var provider = PowerInfoProviderFactory.Create(NullLoggerFactory.Instance);
        Assert.IsType<LinuxPowerInfoProvider>(provider);
    }

    [Fact]
    public void Create_OnNonLinux_ReturnsFallbackProvider()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return; // Skip on Linux

        var provider = PowerInfoProviderFactory.Create(NullLoggerFactory.Instance);
        Assert.IsType<FallbackPowerInfoProvider>(provider);
    }
}
