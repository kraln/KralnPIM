using Microsoft.Extensions.Logging.Abstractions;
using PIM.SystemInfo;

namespace PIM.SystemInfo.Tests;

public class ClockProviderTests
{
    private readonly ClockProvider _provider = new(NullLogger<ClockProvider>.Instance);

    [Fact]
    public void GetCurrent_UtcTimezone_ReturnsCorrectZone()
    {
        var result = _provider.GetCurrent(["UTC"]);

        Assert.Single(result.Zones);
        Assert.Equal("UTC", result.Zones[0].TimezoneId);
    }

    [Fact]
    public void GetCurrent_MultipleTimezones_ReturnsAll()
    {
        var result = _provider.GetCurrent(["UTC", "America/New_York", "Europe/London"]);

        Assert.Equal(3, result.Zones.Count);
        Assert.Equal("UTC", result.Zones[0].TimezoneId);
        Assert.Equal("America/New_York", result.Zones[1].TimezoneId);
        Assert.Equal("Europe/London", result.Zones[2].TimezoneId);
    }

    [Fact]
    public void GetCurrent_InvalidTimezone_SkipsIt()
    {
        var result = _provider.GetCurrent(["UTC", "Not/A/Real/Timezone", "Europe/London"]);

        Assert.Equal(2, result.Zones.Count);
        Assert.Equal("UTC", result.Zones[0].TimezoneId);
        Assert.Equal("Europe/London", result.Zones[1].TimezoneId);
    }

    [Fact]
    public void GetCurrent_EmptyList_ReturnsEmptyZones()
    {
        var result = _provider.GetCurrent([]);

        Assert.Empty(result.Zones);
    }

    [Fact]
    public void GetCurrent_UtcTime_IsReasonablyClose()
    {
        var before = DateTimeOffset.UtcNow;
        var result = _provider.GetCurrent(["UTC"]);
        var after = DateTimeOffset.UtcNow;

        var utcTime = result.Zones[0].CurrentTime;
        Assert.InRange(utcTime, before, after.AddSeconds(1));
    }

    [Fact]
    public void GetCurrent_NewYork_HasOffset()
    {
        var result = _provider.GetCurrent(["America/New_York"]);

        var zone = result.Zones[0];
        // New York is UTC-5 or UTC-4 depending on DST
        var offset = zone.CurrentTime.Offset;
        Assert.True(
            offset == TimeSpan.FromHours(-5) || offset == TimeSpan.FromHours(-4),
            $"Expected UTC-5 or UTC-4, got {offset}");
    }

    [Fact]
    public void GetCurrent_HasLabel()
    {
        var result = _provider.GetCurrent(["America/New_York"]);

        Assert.False(string.IsNullOrEmpty(result.Zones[0].Label));
    }

    [Fact]
    public void GetCurrent_AllInvalid_ReturnsEmpty()
    {
        var result = _provider.GetCurrent(["Fake/Zone1", "Fake/Zone2"]);

        Assert.Empty(result.Zones);
    }
}
