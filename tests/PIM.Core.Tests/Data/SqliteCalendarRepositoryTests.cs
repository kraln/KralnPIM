using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Tests.TestHelpers;

namespace PIM.Core.Tests.Data;

public class SqliteCalendarRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly SqliteCalendarRepository _repo;

    public SqliteCalendarRepositoryTests()
    {
        _repo = new SqliteCalendarRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private static CalendarEvent MakeEvent(
        string eventId = "evt-001",
        string accountId = "acc-1",
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string summary = "Test Event")
    {
        var s = start ?? new DateTimeOffset(2025, 1, 6, 9, 0, 0, TimeSpan.Zero);
        return new CalendarEvent(
            EventId: eventId,
            AccountId: accountId,
            CalendarId: "cal-1",
            Summary: summary,
            Description: "A test event",
            Start: s,
            End: end ?? s.AddHours(1),
            IsAllDay: false,
            Location: "Room A",
            Invitees: ["alice@example.com"],
            RecurrenceRule: null,
            Status: EventStatus.Confirmed
        );
    }

    [Fact]
    public async Task UpsertEvents_SingleEvent_CanRetrieve()
    {
        var evt = MakeEvent();
        await _repo.UpsertEventsAsync([evt]);

        var results = await _repo.GetEventsInRangeAsync(
            evt.Start.AddHours(-1), evt.End.AddHours(1));
        Assert.Single(results);
        Assert.Equal("evt-001", results[0].EventId);
        Assert.Equal("Test Event", results[0].Summary);
        Assert.Equal(EventStatus.Confirmed, results[0].Status);
    }

    [Fact]
    public async Task UpsertEvents_DuplicateId_Updates()
    {
        await _repo.UpsertEventsAsync([MakeEvent(summary: "Original")]);
        await _repo.UpsertEventsAsync([MakeEvent(summary: "Updated")]);

        var results = await _repo.GetEventsInRangeAsync(
            new DateTimeOffset(2025, 1, 6, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 7, 0, 0, 0, TimeSpan.Zero));
        Assert.Single(results);
        Assert.Equal("Updated", results[0].Summary);
    }

    [Fact]
    public async Task GetEventsInRange_ReturnsOverlapping()
    {
        // Event spans 9:00-10:00, query 9:30-11:00 should find it
        await _repo.UpsertEventsAsync([MakeEvent()]);

        var results = await _repo.GetEventsInRangeAsync(
            new DateTimeOffset(2025, 1, 6, 9, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 6, 11, 0, 0, TimeSpan.Zero));
        Assert.Single(results);
    }

    [Fact]
    public async Task GetEventsInRange_ExcludesOutOfRange()
    {
        await _repo.UpsertEventsAsync([MakeEvent()]);

        var results = await _repo.GetEventsInRangeAsync(
            new DateTimeOffset(2025, 1, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 8, 0, 0, 0, TimeSpan.Zero));
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetEventsInRange_FilterByAccountId()
    {
        await _repo.UpsertEventsAsync([
            MakeEvent("evt-1", accountId: "acc-1"),
            MakeEvent("evt-2", accountId: "acc-2")
        ]);

        var range = (
            new DateTimeOffset(2025, 1, 6, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 7, 0, 0, 0, TimeSpan.Zero)
        );

        var results = await _repo.GetEventsInRangeAsync(range.Item1, range.Item2, "acc-1");
        Assert.Single(results);
        Assert.Equal("acc-1", results[0].AccountId);
    }

    [Fact]
    public async Task GetEventsInRange_EmptyDatabase_ReturnsEmptyList()
    {
        var results = await _repo.GetEventsInRangeAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        Assert.Empty(results);
    }

    [Fact]
    public async Task DeleteEvent_Removes()
    {
        await _repo.UpsertEventsAsync([MakeEvent()]);
        await _repo.DeleteEventAsync("evt-001");

        var results = await _repo.GetEventsInRangeAsync(
            new DateTimeOffset(2025, 1, 6, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 7, 0, 0, 0, TimeSpan.Zero));
        Assert.Empty(results);
    }

    [Fact]
    public async Task DeleteEvent_NonExistent_NoError()
    {
        await _repo.DeleteEventAsync("nonexistent");
        // Should not throw
    }

    [Fact]
    public async Task PurgeOlderThan_RemovesOldEvents()
    {
        await _repo.UpsertEventsAsync([
            MakeEvent("evt-old",
                start: new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero),
                end: new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero)),
            MakeEvent("evt-new",
                start: new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero),
                end: new DateTimeOffset(2025, 6, 1, 10, 0, 0, TimeSpan.Zero))
        ]);

        await _repo.PurgeOlderThanAsync(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var all = await _repo.GetEventsInRangeAsync(
            new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Single(all);
        Assert.Equal("evt-new", all[0].EventId);
    }
}
