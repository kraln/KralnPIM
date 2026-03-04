using System.Net.Http.Json;
using System.Text.Json;
using PIM.Core.Models;
using PIM.Core.Serialization;
using PIM.Tui.Models;

namespace PIM.Tui.Client;

public sealed class PimApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;

    public PimApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _jsonOptions.TypeInfoResolverChain.Add(PimJsonContext.Default);
        _jsonOptions.TypeInfoResolverChain.Add(TuiJsonContext.Default);
    }

    internal PimApiClient(HttpClient http)
    {
        _http = http;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _jsonOptions.TypeInfoResolverChain.Add(PimJsonContext.Default);
        _jsonOptions.TypeInfoResolverChain.Add(TuiJsonContext.Default);
    }

    // Mail

    public async Task<List<EmailHeader>> ListMailAsync(
        string? accountId = null, bool? isRead = null, bool? isFlagged = null,
        int offset = 0, int limit = 50, CancellationToken ct = default)
    {
        var url = BuildQuery("/api/mail",
            ("accountId", accountId),
            ("isRead", isRead?.ToString().ToLowerInvariant()),
            ("isFlagged", isFlagged?.ToString().ToLowerInvariant()),
            ("offset", offset.ToString()),
            ("limit", limit.ToString()));

        return await GetAsync<List<EmailHeader>>(url, ct) ?? [];
    }

    public async Task<List<AccountOverview>> GetAccountsAsync(CancellationToken ct = default) =>
        await GetAsync<List<AccountOverview>>("/api/mail/accounts", ct) ?? [];

    public async Task<MailDetail?> GetMailDetailAsync(string messageId, CancellationToken ct = default) =>
        await GetAsync<MailDetail>($"/api/mail/{Uri.EscapeDataString(messageId)}", ct);

    public async Task SetMailFlagsAsync(string messageId, MailFlagPatch patch, CancellationToken ct = default)
    {
        var url = $"/api/mail/{Uri.EscapeDataString(messageId)}";
        using var response = await _http.PatchAsJsonAsync(url, patch, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendMailAsync(OutboundEmail email, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("/api/mail/send", email, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AttachmentDownloadResult?> DownloadAttachmentAsync(
        string messageId, string filename, CancellationToken ct = default) =>
        await GetAsync<AttachmentDownloadResult>(
            $"/api/mail/attachment/{Uri.EscapeDataString(messageId)}/{Uri.EscapeDataString(filename)}", ct);

    // Calendar

    public async Task<List<CalendarEvent>> GetEventsAsync(
        DateTimeOffset start, DateTimeOffset end, string? accountId = null, CancellationToken ct = default)
    {
        var url = BuildQuery("/api/calendar/events",
            ("start", start.ToString("o")),
            ("end", end.ToString("o")),
            ("accountId", accountId));

        return await GetAsync<List<CalendarEvent>>(url, ct) ?? [];
    }

    public async Task CreateEventAsync(CalendarEvent evt, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("/api/calendar/events", evt, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateEventAsync(CalendarEvent evt, CancellationToken ct = default)
    {
        using var response = await _http.PutAsJsonAsync(
            $"/api/calendar/events/{Uri.EscapeDataString(evt.EventId)}", evt, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteEventAsync(string eventId, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync(
            $"/api/calendar/events/{Uri.EscapeDataString(eventId)}", ct);
        response.EnsureSuccessStatusCode();
    }

    // Search

    public async Task<SearchResult?> SearchLocalAsync(string query, string? scope = null, CancellationToken ct = default)
    {
        var url = BuildQuery("/api/search",
            ("q", query),
            ("scope", scope));

        return await GetAsync<SearchResult>(url, ct);
    }

    public async Task<SearchResult?> SearchDeepAsync(string query, string? scope = null, CancellationToken ct = default)
    {
        var request = new DeepSearchRequest(query, scope);
        using var response = await _http.PostAsJsonAsync("/api/search/deep", request, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SearchResult>(_jsonOptions, ct);
    }

    // System

    public async Task<PowerInfo?> GetPowerAsync(CancellationToken ct = default) =>
        await GetAsync<PowerInfo>("/api/system/power", ct);

    public async Task<WeatherInfo?> GetWeatherAsync(CancellationToken ct = default) =>
        await GetAsync<WeatherInfo>("/api/system/weather", ct);

    public async Task<ClockInfo?> GetClockAsync(CancellationToken ct = default) =>
        await GetAsync<ClockInfo>("/api/system/clock", ct);

    public async Task<SystemStatus?> GetStatusAsync(CancellationToken ct = default) =>
        await GetAsync<SystemStatus>("/api/system/status", ct);

    // Helpers

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, ct);
    }

    private static string BuildQuery(string path, params (string key, string? value)[] parameters)
    {
        var pairs = parameters
            .Where(p => p.value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.key)}={Uri.EscapeDataString(p.value!)}");

        var query = string.Join("&", pairs);
        return query.Length > 0 ? $"{path}?{query}" : path;
    }

    public void Dispose() => _http.Dispose();
}
