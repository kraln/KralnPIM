using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.SystemInfo;

public sealed class FallbackPowerInfoProvider : IPowerInfoProvider
{
    private static readonly PowerInfo NoBattery = new(-1, null, null);

    public Task<PowerInfo> GetAsync(CancellationToken ct) =>
        Task.FromResult(NoBattery);
}
