using PIM.Core.Models;

namespace PIM.Core.Providers;

public interface ISearchService
{
    Task<SearchResult> LocalSearchAsync(string query, SearchScope scope, int limit = 50, CancellationToken ct = default);
    Task<SearchResult> DeepSearchAsync(string query, SearchScope scope, CancellationToken ct = default);
}
