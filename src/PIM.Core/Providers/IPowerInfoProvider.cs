using PIM.Core.Models;

namespace PIM.Core.Providers;

public interface IPowerInfoProvider
{
    Task<PowerInfo> GetAsync(CancellationToken ct);
}
