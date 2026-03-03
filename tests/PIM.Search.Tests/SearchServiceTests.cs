using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;
using PIM.Search.Tests.TestHelpers;

namespace PIM.Search.Tests;

public class SearchServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly SqliteEmailRepository _emailRepo;
    private readonly SqliteCalendarRepository _calendarRepo;
    private readonly IMailProvider _mockProvider1;
    private readonly IMailProvider _mockProvider2;

    public SearchServiceTests()
    {
        _emailRepo = new SqliteEmailRepository(_db.Factory);
        _calendarRepo = new SqliteCalendarRepository(_db.Factory);
        _mockProvider1 = Substitute.For<IMailProvider>();
        _mockProvider1.AccountId.Returns("account-1");
        _mockProvider2 = Substitute.For<IMailProvider>();
        _mockProvider2.AccountId.Returns("account-2");
    }

    public void Dispose() => _db.Dispose();

    private SearchService CreateService(IReadOnlyList<IMailProvider>? providers = null)
    {
        return new SearchService(
            _emailRepo,
            _calendarRepo,
            providers ?? [_mockProvider1, _mockProvider2],
            NullLogger<SearchService>.Instance);
    }

    private static EmailHeader MakeHeader(
        string messageId, string subject, string fromAddress = "alice@example.com",
        DateTimeOffset? date = null)
    {
        return new EmailHeader(
            MessageId: messageId,
            AccountId: "acc-1",
            FolderId: "INBOX",
            Subject: subject,
            FromAddress: fromAddress,
            FromDisplayName: "Alice",
            ToAddresses: ["bob@example.com"],
            CcAddresses: [],
            Date: date ?? new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero),
            IsRead: false,
            IsFlagged: false,
            PlainTextSnippet: null,
            Attachments: []);
    }

    private static CalendarEvent MakeEvent(
        string eventId, string summary, string? description = null, string? location = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new CalendarEvent(
            EventId: eventId,
            AccountId: "acc-1",
            CalendarId: "cal-1",
            Summary: summary,
            Description: description,
            Start: now.AddDays(1),
            End: now.AddDays(1).AddHours(1),
            IsAllDay: false,
            Location: location,
            Invitees: [],
            RecurrenceRule: null,
            Status: EventStatus.Confirmed);
    }

    // --- Local search: FTS5 ---

    [Fact]
    public async Task LocalSearch_BySubject_ReturnsMatchingEmails()
    {
        await _emailRepo.UpsertHeadersAsync([
            MakeHeader("msg-1", "Quarterly Revenue Report"),
            MakeHeader("msg-2", "Lunch Plans"),
            MakeHeader("msg-3", "Revenue Projections")
        ]);

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("revenue", SearchScope.Mail);

        Assert.Equal(2, result.Emails.Count);
        Assert.Contains(result.Emails, e => e.MessageId == "msg-1");
        Assert.Contains(result.Emails, e => e.MessageId == "msg-3");
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task LocalSearch_ByBody_ReturnsMatchingEmails()
    {
        await _emailRepo.UpsertHeadersAsync([
            MakeHeader("msg-1", "Subject A"),
            MakeHeader("msg-2", "Subject B")
        ]);
        await _emailRepo.UpsertBodyAsync("msg-1", "Detailed quarterly financial analysis for the board.");
        await _emailRepo.UpsertBodyAsync("msg-2", "Quick reminder about lunch.");

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("quarterly financial", SearchScope.Mail);

        Assert.Single(result.Emails);
        Assert.Equal("msg-1", result.Emails[0].MessageId);
    }

    [Fact]
    public async Task LocalSearch_EmptyQuery_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.LocalSearchAsync("", SearchScope.Mail));
    }

    [Fact]
    public async Task LocalSearch_NoMatches_ReturnsEmptyResult()
    {
        await _emailRepo.UpsertHeadersAsync([MakeHeader("msg-1", "Hello World")]);

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("zzzznonexistent", SearchScope.Mail);

        Assert.Empty(result.Emails);
    }

    [Fact]
    public async Task LocalSearch_RespectsLimit()
    {
        var headers = Enumerable.Range(1, 20)
            .Select(i => MakeHeader($"msg-{i}", $"Revenue item {i}"))
            .ToList();
        await _emailRepo.UpsertHeadersAsync(headers);

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("revenue", SearchScope.Mail, limit: 5);

        Assert.Equal(5, result.Emails.Count);
    }

    [Fact]
    public async Task LocalSearch_ScopeMail_ReturnsOnlyEmails()
    {
        await _emailRepo.UpsertHeadersAsync([MakeHeader("msg-1", "Revenue Report")]);
        await _calendarRepo.UpsertEventsAsync([MakeEvent("evt-1", "Revenue Meeting")]);

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("revenue", SearchScope.Mail);

        Assert.NotEmpty(result.Emails);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task LocalSearch_ScopeCalendar_ReturnsOnlyEvents()
    {
        await _emailRepo.UpsertHeadersAsync([MakeHeader("msg-1", "Revenue Report")]);
        await _calendarRepo.UpsertEventsAsync([MakeEvent("evt-1", "Revenue Meeting")]);

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("revenue", SearchScope.Calendar);

        Assert.Empty(result.Emails);
        Assert.NotEmpty(result.Events);
    }

    [Fact]
    public async Task LocalSearch_ScopeAll_ReturnsBoth()
    {
        await _emailRepo.UpsertHeadersAsync([MakeHeader("msg-1", "Revenue Report")]);
        await _calendarRepo.UpsertEventsAsync([MakeEvent("evt-1", "Revenue Meeting")]);

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("revenue", SearchScope.All);

        Assert.NotEmpty(result.Emails);
        Assert.NotEmpty(result.Events);
    }

    // --- Local search: Calendar ---

    [Fact]
    public async Task LocalSearch_Calendar_MatchesSummary()
    {
        await _calendarRepo.UpsertEventsAsync([
            MakeEvent("evt-1", "Team Standup"),
            MakeEvent("evt-2", "Board Meeting")
        ]);

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("standup", SearchScope.Calendar);

        Assert.Single(result.Events);
        Assert.Equal("evt-1", result.Events[0].EventId);
    }

    [Fact]
    public async Task LocalSearch_Calendar_MatchesDescription()
    {
        await _calendarRepo.UpsertEventsAsync([
            MakeEvent("evt-1", "Meeting", description: "Discuss the kubernetes migration plan")
        ]);

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("kubernetes", SearchScope.Calendar);

        Assert.Single(result.Events);
    }

    [Fact]
    public async Task LocalSearch_Calendar_MatchesLocation()
    {
        await _calendarRepo.UpsertEventsAsync([
            MakeEvent("evt-1", "Offsite", location: "Downtown Conference Center")
        ]);

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("downtown", SearchScope.Calendar);

        Assert.Single(result.Events);
    }

    [Fact]
    public async Task LocalSearch_Calendar_CaseInsensitive()
    {
        await _calendarRepo.UpsertEventsAsync([
            MakeEvent("evt-1", "IMPORTANT MEETING")
        ]);

        var svc = CreateService();
        var result = await svc.LocalSearchAsync("important meeting", SearchScope.Calendar);

        Assert.Single(result.Events);
    }

    // --- Deep search ---

    [Fact]
    public async Task DeepSearch_FansOutToAllProviders()
    {
        _mockProvider1.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Returns([MakeHeader("msg-1", "Result 1")]);
        _mockProvider2.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Returns([MakeHeader("msg-2", "Result 2")]);

        var svc = CreateService();
        var result = await svc.DeepSearchAsync("test", SearchScope.Mail);

        Assert.Equal(2, result.Emails.Count);
        await _mockProvider1.Received(1).RemoteSearchAsync("test", Arg.Any<CancellationToken>());
        await _mockProvider2.Received(1).RemoteSearchAsync("test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeepSearch_DeduplicatesByMessageId()
    {
        var shared = MakeHeader("shared-id", "Shared Email");
        _mockProvider1.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Returns([shared]);
        _mockProvider2.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Returns([shared]);

        var svc = CreateService();
        var result = await svc.DeepSearchAsync("test", SearchScope.Mail);

        Assert.Single(result.Emails);
        Assert.Equal("shared-id", result.Emails[0].MessageId);
    }

    [Fact]
    public async Task DeepSearch_SortsByDateDescending()
    {
        var older = MakeHeader("msg-old", "Old", date: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = MakeHeader("msg-new", "New", date: new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));

        _mockProvider1.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Returns([older]);
        _mockProvider2.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Returns([newer]);

        var svc = CreateService();
        var result = await svc.DeepSearchAsync("test", SearchScope.Mail);

        Assert.Equal("msg-new", result.Emails[0].MessageId);
        Assert.Equal("msg-old", result.Emails[1].MessageId);
    }

    [Fact]
    public async Task DeepSearch_ProviderFailure_ReturnsPartialResults()
    {
        _mockProvider1.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Returns([MakeHeader("msg-1", "Result 1")]);
        _mockProvider2.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Auth expired"));

        var svc = CreateService();
        var result = await svc.DeepSearchAsync("test", SearchScope.Mail);

        Assert.Single(result.Emails);
        Assert.Equal("msg-1", result.Emails[0].MessageId);
    }

    [Fact]
    public async Task DeepSearch_AllProvidersFail_ReturnsEmptyResult()
    {
        _mockProvider1.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Fail 1"));
        _mockProvider2.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Fail 2"));

        var svc = CreateService();
        var result = await svc.DeepSearchAsync("test", SearchScope.Mail);

        Assert.Empty(result.Emails);
    }

    [Fact]
    public async Task DeepSearch_ScopeMail_SkipsCalendar()
    {
        _mockProvider1.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Returns([MakeHeader("msg-1", "Result")]);
        _mockProvider2.RemoteSearchAsync("test", Arg.Any<CancellationToken>())
            .Returns(new List<EmailHeader>());

        // Insert a calendar event that would match
        await _calendarRepo.UpsertEventsAsync([MakeEvent("evt-1", "Test Event")]);

        var svc = CreateService();
        var result = await svc.DeepSearchAsync("test", SearchScope.Mail);

        Assert.NotEmpty(result.Emails);
        Assert.Empty(result.Events);
    }
}
