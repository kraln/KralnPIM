using Microsoft.Extensions.Logging;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;

namespace PIM.Search;

public sealed class SearchService : ISearchService
{
    private readonly IEmailRepository _emailRepo;
    private readonly ICalendarRepository _calendarRepo;
    private readonly IReadOnlyList<IMailProvider> _mailProviders;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        IEmailRepository emailRepo,
        ICalendarRepository calendarRepo,
        IReadOnlyList<IMailProvider> mailProviders,
        ILogger<SearchService> logger)
    {
        _emailRepo = emailRepo;
        _calendarRepo = calendarRepo;
        _mailProviders = mailProviders;
        _logger = logger;
    }

    public async Task<SearchResult> LocalSearchAsync(
        string query, SearchScope scope, int limit = 50, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var emails = new List<EmailHeader>();
        var events = new List<CalendarEvent>();

        if (scope is SearchScope.All or SearchScope.Mail)
            emails = await _emailRepo.SearchAsync(query, limit, ct);

        if (scope is SearchScope.All or SearchScope.Calendar)
            events = await SearchCalendarLocalAsync(query, limit, ct);

        return new SearchResult(emails, events);
    }

    public async Task<SearchResult> DeepSearchAsync(
        string query, SearchScope scope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var emails = new List<EmailHeader>();
        var events = new List<CalendarEvent>();

        if (scope is SearchScope.All or SearchScope.Mail)
            emails = await DeepSearchEmailAsync(query, ct);

        // Calendar deep search deferred — local window is sufficient
        if (scope is SearchScope.All or SearchScope.Calendar)
            events = await SearchCalendarLocalAsync(query, 50, ct);

        return new SearchResult(emails, events);
    }

    private async Task<List<CalendarEvent>> SearchCalendarLocalAsync(
        string query, int limit, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rangeStart = now.AddMonths(-6);
        var rangeEnd = now.AddMonths(6);

        var allEvents = await _calendarRepo.GetEventsInRangeAsync(rangeStart, rangeEnd, ct: ct);

        return allEvents
            .Where(e => MatchesCalendarQuery(e, query))
            .Take(limit)
            .ToList();
    }

    private async Task<List<EmailHeader>> DeepSearchEmailAsync(
        string query, CancellationToken ct)
    {
        var tasks = _mailProviders.Select(p => SearchProviderSafe(p, query, ct));
        var allResults = await Task.WhenAll(tasks);

        var seen = new HashSet<string>();
        var deduped = new List<EmailHeader>();

        foreach (var batch in allResults)
        {
            foreach (var header in batch)
            {
                if (seen.Add(header.MessageId))
                    deduped.Add(header);
            }
        }

        deduped.Sort((a, b) => b.Date.CompareTo(a.Date));
        return deduped;
    }

    private async Task<List<EmailHeader>> SearchProviderSafe(
        IMailProvider provider, string query, CancellationToken ct)
    {
        try
        {
            return await provider.RemoteSearchAsync(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remote search failed for account {AccountId}", provider.AccountId);
            return [];
        }
    }

    private static bool MatchesCalendarQuery(CalendarEvent evt, string query)
    {
        return Contains(evt.Summary, query)
            || Contains(evt.Description, query)
            || Contains(evt.Location, query);
    }

    private static bool Contains(string? text, string query)
    {
        return text is not null
            && text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
