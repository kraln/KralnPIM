using System.Net.Http.Json;
using System.Text.Json;
using PIM.Tui.Models;

namespace PIM.Tui.Client;

internal sealed class TihApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;

    public TihApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _jsonOptions.TypeInfoResolverChain.Add(TuiJsonContext.Default);
    }

    public async Task<TihResponse?> GetTodayAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<TihResponse>("/api/today", _jsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
