using Microsoft.Extensions.Logging.Abstractions;
using PIM.SystemInfo;

namespace PIM.SystemInfo.Tests;

public class LinuxPowerInfoProviderTests
{
    [Fact]
    public async Task GetAsync_NoBatteryDirectory_ReturnsNoBattery()
    {
        var provider = new LinuxPowerInfoProvider(NullLogger<LinuxPowerInfoProvider>.Instance)
        {
            BasePath = "/tmp/pim-test-nonexistent-battery-path",
        };

        var result = await provider.GetAsync(CancellationToken.None);

        Assert.Equal(-1, result.BatteryPercent);
        Assert.Null(result.TimeRemaining);
        Assert.Null(result.DrainWatts);
    }

    [Fact]
    public async Task GetAsync_WithCapacityFile_ReadsBatteryPercent()
    {
        var tempDir = CreateTempBatteryDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "capacity"), "87\n");
            File.WriteAllText(Path.Combine(tempDir, "status"), "Discharging\n");

            var provider = new LinuxPowerInfoProvider(NullLogger<LinuxPowerInfoProvider>.Instance)
            {
                BasePath = tempDir,
            };

            var result = await provider.GetAsync(CancellationToken.None);

            Assert.Equal(87, result.BatteryPercent);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetAsync_WithPowerNow_ReadsDrainWatts()
    {
        var tempDir = CreateTempBatteryDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "capacity"), "50\n");
            File.WriteAllText(Path.Combine(tempDir, "status"), "Discharging\n");
            // 8W = 8_000_000 µW
            File.WriteAllText(Path.Combine(tempDir, "power_now"), "8000000\n");
            // 40 Wh = 40_000_000 µWh
            File.WriteAllText(Path.Combine(tempDir, "energy_now"), "40000000\n");

            var provider = new LinuxPowerInfoProvider(NullLogger<LinuxPowerInfoProvider>.Instance)
            {
                BasePath = tempDir,
            };

            var result = await provider.GetAsync(CancellationToken.None);

            Assert.Equal(50, result.BatteryPercent);
            Assert.Equal(8.0, result.DrainWatts);
            Assert.Equal("5h 00m", result.TimeRemaining);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetAsync_Charging_NoTimeRemaining()
    {
        var tempDir = CreateTempBatteryDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "capacity"), "75\n");
            File.WriteAllText(Path.Combine(tempDir, "status"), "Charging\n");
            File.WriteAllText(Path.Combine(tempDir, "power_now"), "5000000\n");
            File.WriteAllText(Path.Combine(tempDir, "energy_now"), "30000000\n");

            var provider = new LinuxPowerInfoProvider(NullLogger<LinuxPowerInfoProvider>.Instance)
            {
                BasePath = tempDir,
            };

            var result = await provider.GetAsync(CancellationToken.None);

            Assert.Equal(75, result.BatteryPercent);
            Assert.Equal(5.0, result.DrainWatts);
            Assert.Null(result.TimeRemaining);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetAsync_EmptyCapacity_ReturnsNoBattery()
    {
        var tempDir = CreateTempBatteryDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "capacity"), "\n");

            var provider = new LinuxPowerInfoProvider(NullLogger<LinuxPowerInfoProvider>.Instance)
            {
                BasePath = tempDir,
            };

            var result = await provider.GetAsync(CancellationToken.None);

            Assert.Equal(-1, result.BatteryPercent);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetAsync_VoltageAndCurrent_ComputesDrainWatts()
    {
        var tempDir = CreateTempBatteryDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "capacity"), "60\n");
            File.WriteAllText(Path.Combine(tempDir, "status"), "Discharging\n");
            // 12V = 12_000_000 µV, 1A = 1_000_000 µA → 12W
            File.WriteAllText(Path.Combine(tempDir, "voltage_now"), "12000000\n");
            File.WriteAllText(Path.Combine(tempDir, "current_now"), "1000000\n");

            var provider = new LinuxPowerInfoProvider(NullLogger<LinuxPowerInfoProvider>.Instance)
            {
                BasePath = tempDir,
            };

            var result = await provider.GetAsync(CancellationToken.None);

            Assert.Equal(60, result.BatteryPercent);
            Assert.Equal(12.0, result.DrainWatts);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FallbackProvider_ReturnsNoBattery()
    {
        var provider = new FallbackPowerInfoProvider();
        var result = await provider.GetAsync(CancellationToken.None);

        Assert.Equal(-1, result.BatteryPercent);
        Assert.Null(result.TimeRemaining);
        Assert.Null(result.DrainWatts);
    }

    private static string CreateTempBatteryDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pim-battery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
