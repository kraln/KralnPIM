using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;
using PIM.Server.Models;
using PIM.Server.Registration;
using PIM.Server.Services;

namespace PIM.Server.Tests;

public class FreeBusySinkServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 5, 11, 9, 0, 0, TimeSpan.Zero);

    private static CalendarEvent Event(
        string id, DateTimeOffset start, DateTimeOffset end,
        bool allDay = false, EventStatus status = EventStatus.Confirmed,
        string accountId = "acc-1", string calendarId = "cal-source",
        Transparency transparency = Transparency.Busy) =>
        new(id, accountId, calendarId, "Source", null, start, end, allDay, null, [], null, status, transparency);

    private static FreeBusySinkService.BusyBlock Block(DateTimeOffset start, DateTimeOffset end) => new(start, end);

    [Fact]
    public void Coalesce_OverlappingEventsMergeIntoOneBlock()
    {
        var events = new[]
        {
            Event("a", BaseTime, BaseTime.AddHours(1)),
            Event("b", BaseTime.AddMinutes(30), BaseTime.AddHours(2)),
        };

        var blocks = FreeBusySinkService.Coalesce(events, TimeZoneInfo.Utc);

        Assert.Single(blocks);
        Assert.Equal(BaseTime, blocks[0].Start);
        Assert.Equal(BaseTime.AddHours(2), blocks[0].End);
    }

    [Fact]
    public void Coalesce_AdjacentEventsWithinFiveMinutes_Bridge()
    {
        var events = new[]
        {
            Event("a", BaseTime, BaseTime.AddHours(1)),
            Event("b", BaseTime.AddHours(1).AddMinutes(4), BaseTime.AddHours(2)),
        };

        var blocks = FreeBusySinkService.Coalesce(events, TimeZoneInfo.Utc);

        Assert.Single(blocks);
        Assert.Equal(BaseTime, blocks[0].Start);
        Assert.Equal(BaseTime.AddHours(2), blocks[0].End);
    }

    [Fact]
    public void Coalesce_GapsLargerThanFiveMinutes_StayDistinct()
    {
        var events = new[]
        {
            Event("a", BaseTime, BaseTime.AddHours(1)),
            Event("b", BaseTime.AddHours(1).AddMinutes(6), BaseTime.AddHours(2)),
        };

        var blocks = FreeBusySinkService.Coalesce(events, TimeZoneInfo.Utc);

        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void Coalesce_AllDayEvent_ExpandsToFullDayInLocalTz()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var allDay = Event(
            "a",
            new DateTimeOffset(2026, 5, 11, 0, 0, 0, tz.GetUtcOffset(new DateTime(2026, 5, 11))),
            new DateTimeOffset(2026, 5, 12, 0, 0, 0, tz.GetUtcOffset(new DateTime(2026, 5, 12))),
            allDay: true);

        var blocks = FreeBusySinkService.Coalesce([allDay], tz);

        Assert.Single(blocks);
        Assert.Equal(24, (blocks[0].End - blocks[0].Start).TotalHours);
    }

    [Fact]
    public void Coalesce_OutOfOrderInput_StillProducesSortedMergedBlocks()
    {
        var events = new[]
        {
            Event("c", BaseTime.AddHours(4), BaseTime.AddHours(5)),
            Event("a", BaseTime, BaseTime.AddHours(1)),
            Event("b", BaseTime.AddMinutes(30), BaseTime.AddHours(2)),
        };

        var blocks = FreeBusySinkService.Coalesce(events, TimeZoneInfo.Utc);

        Assert.Equal(2, blocks.Count);
        Assert.Equal(BaseTime, blocks[0].Start);
        Assert.Equal(BaseTime.AddHours(2), blocks[0].End);
        Assert.Equal(BaseTime.AddHours(4), blocks[1].Start);
    }

    [Fact]
    public void HashBlocks_DifferentBlocks_DifferentHashes()
    {
        var a = new[] { Block(BaseTime, BaseTime.AddHours(1)) };
        var b = new[] { Block(BaseTime, BaseTime.AddHours(2)) };

        Assert.NotEqual(FreeBusySinkService.HashBlocks(a), FreeBusySinkService.HashBlocks(b));
    }

    [Fact]
    public void HashBlocks_SameBlocks_SameHash()
    {
        var a = new[] { Block(BaseTime, BaseTime.AddHours(1)), Block(BaseTime.AddHours(2), BaseTime.AddHours(3)) };
        var b = new[] { Block(BaseTime, BaseTime.AddHours(1)), Block(BaseTime.AddHours(2), BaseTime.AddHours(3)) };

        Assert.Equal(FreeBusySinkService.HashBlocks(a), FreeBusySinkService.HashBlocks(b));
    }

    [Fact]
    public async Task RefreshAsync_NoSinks_DoesNothing()
    {
        var (svc, registry, calendarRepo, _, _) = BuildService();
        registry.Sinks.Returns(new List<SinkInfo>());

        await svc.RefreshAsync(CancellationToken.None);

        await calendarRepo.DidNotReceive().GetEventsInRangeAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_HashUnchanged_SkipsApiCalls()
    {
        var (svc, registry, calendarRepo, syncStateRepo, sinkProvider) = BuildService();
        var events = new[] { Event("a", BaseTime, BaseTime.AddHours(1)) };
        calendarRepo.GetEventsInRangeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>())
            .Returns(events.ToList());

        var blocks = FreeBusySinkService.Coalesce(events, TimeZoneInfo.Utc);
        var hash = FreeBusySinkService.HashBlocks(blocks);
        var shadow = new FreeBusyShadow(hash, ["existing-id"]);
        var shadowJson = JsonSerializer.Serialize(shadow, ServerJsonContext.Default.FreeBusyShadow);
        syncStateRepo.GetAsync("acc-1", "freebusy-sink:cal-sink", Arg.Any<CancellationToken>())
            .Returns(((DateTimeOffset?)DateTimeOffset.UtcNow, (string?)shadowJson));

        await svc.RefreshAsync(CancellationToken.None);

        await sinkProvider.DidNotReceive().DeleteEventAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await sinkProvider.DidNotReceive().CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>());
        await syncStateRepo.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_HashChanged_DeletesShadowAndCreatesNewBlocks()
    {
        var (svc, registry, calendarRepo, syncStateRepo, sinkProvider) = BuildService();
        var events = new[]
        {
            Event("a", BaseTime, BaseTime.AddHours(1)),
            Event("b", BaseTime.AddHours(3), BaseTime.AddHours(4)),
        };
        calendarRepo.GetEventsInRangeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>())
            .Returns(events.ToList());

        var staleShadow = new FreeBusyShadow("stale-hash", ["old-1", "old-2"]);
        syncStateRepo.GetAsync("acc-1", "freebusy-sink:cal-sink", Arg.Any<CancellationToken>())
            .Returns(((DateTimeOffset?)DateTimeOffset.UtcNow,
                      (string?)JsonSerializer.Serialize(staleShadow, ServerJsonContext.Default.FreeBusyShadow)));

        var assigned = 0;
        sinkProvider.CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => $"new-{++assigned}");

        await svc.RefreshAsync(CancellationToken.None);

        await sinkProvider.Received(1).DeleteEventAsync("old-1", Arg.Any<CancellationToken>());
        await sinkProvider.Received(1).DeleteEventAsync("old-2", Arg.Any<CancellationToken>());
        await sinkProvider.Received(2).CreateEventAsync(
            Arg.Is<CalendarEvent>(e => e.Summary == "Busy" && e.Status == EventStatus.Confirmed && e.AccountId == "acc-1" && e.CalendarId == "cal-sink"),
            Arg.Any<CancellationToken>());
        await syncStateRepo.Received(1).SetAsync(
            "acc-1", "freebusy-sink:cal-sink", Arg.Any<DateTimeOffset>(),
            Arg.Is<string>(s => s != null && s.Contains("new-1") && s.Contains("new-2")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_SourceEventsFromSink_AreExcluded()
    {
        var (svc, registry, calendarRepo, syncStateRepo, sinkProvider) = BuildService();
        var events = new[]
        {
            Event("a", BaseTime, BaseTime.AddHours(1)),
            Event("loop", BaseTime.AddHours(1), BaseTime.AddHours(2), accountId: "acc-1", calendarId: "cal-sink"),
        };
        calendarRepo.GetEventsInRangeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>())
            .Returns(events.ToList());
        sinkProvider.CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>()).Returns("id-1");

        await svc.RefreshAsync(CancellationToken.None);

        // Only the non-sink event should produce a single 1-hour block (the sink-self event is filtered out).
        await sinkProvider.Received(1).CreateEventAsync(
            Arg.Is<CalendarEvent>(e => e.End - e.Start == TimeSpan.FromHours(1)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_CancelledSourceEvents_AreExcluded()
    {
        var (svc, registry, calendarRepo, syncStateRepo, sinkProvider) = BuildService();
        var events = new[]
        {
            Event("a", BaseTime, BaseTime.AddHours(1)),
            Event("b", BaseTime.AddHours(2), BaseTime.AddHours(3), status: EventStatus.Cancelled),
        };
        calendarRepo.GetEventsInRangeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>())
            .Returns(events.ToList());
        sinkProvider.CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>()).Returns("id-1");

        await svc.RefreshAsync(CancellationToken.None);

        await sinkProvider.Received(1).CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_SinkRequiresReauth_OtherSinksStillRefreshed()
    {
        var registry = Substitute.For<ProviderRegistry>(NullLogger<ProviderRegistry>.Instance);
        var calendarRepo = Substitute.For<ICalendarRepository>();
        var syncStateRepo = Substitute.For<ISyncStateRepository>();
        var sinkA = Substitute.For<ICalendarProvider>();
        var sinkB = Substitute.For<ICalendarProvider>();
        registry.Sinks.Returns(new List<SinkInfo>
        {
            new("acc-1", "cal-a", sinkA),
            new("acc-2", "cal-b", sinkB),
        });
        calendarRepo.GetEventsInRangeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>())
            .Returns([Event("a", BaseTime, BaseTime.AddHours(1))]);
        sinkA.CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>())
            .Throws(new ReauthorizationRequiredException("acc-1"));
        sinkB.CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>()).Returns("id-1");

        var svc = new FreeBusySinkService(registry, calendarRepo, syncStateRepo,
            BuildPimConfig(), NullLogger<FreeBusySinkService>.Instance);

        await svc.RefreshAsync(CancellationToken.None);

        await sinkB.Received(1).CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_FreeSourceEvents_AreExcluded()
    {
        var (svc, registry, calendarRepo, syncStateRepo, sinkProvider) = BuildService();
        var events = new[]
        {
            Event("a", BaseTime, BaseTime.AddHours(1)),
            Event("birthday", BaseTime.AddHours(2), BaseTime.AddHours(3), transparency: Transparency.Free),
        };
        calendarRepo.GetEventsInRangeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>())
            .Returns(events.ToList());
        sinkProvider.CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>()).Returns("id-1");

        await svc.RefreshAsync(CancellationToken.None);

        // Only the busy event produces a block; the free event is filtered out.
        await sinkProvider.Received(1).CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_IgnoreAllDaySink_ExcludesAllDayEvents()
    {
        var registry = Substitute.For<ProviderRegistry>(NullLogger<ProviderRegistry>.Instance);
        var calendarRepo = Substitute.For<ICalendarRepository>();
        var syncStateRepo = Substitute.For<ISyncStateRepository>();
        var sinkProvider = Substitute.For<ICalendarProvider>();
        registry.Sinks.Returns(new List<SinkInfo> { new("acc-1", "cal-sink", sinkProvider, IgnoreAllDay: true) });

        var allDayStart = new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("timed", BaseTime, BaseTime.AddHours(1)),
            Event("allday", allDayStart, allDayStart.AddDays(1), allDay: true),
        };
        calendarRepo.GetEventsInRangeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>())
            .Returns(events.ToList());
        sinkProvider.CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>()).Returns("id-1");

        var svc = new FreeBusySinkService(registry, calendarRepo, syncStateRepo,
            BuildPimConfig(), NullLogger<FreeBusySinkService>.Instance);

        await svc.RefreshAsync(CancellationToken.None);

        // Only the timed event produces a block; the all-day event is excluded.
        await sinkProvider.Received(1).CreateEventAsync(
            Arg.Is<CalendarEvent>(e => e.End - e.Start == TimeSpan.FromHours(1)),
            Arg.Any<CancellationToken>());
    }

    private static (FreeBusySinkService Svc, ProviderRegistry Registry, ICalendarRepository CalRepo, ISyncStateRepository SyncStateRepo, ICalendarProvider SinkProvider) BuildService()
    {
        var registry = Substitute.For<ProviderRegistry>(NullLogger<ProviderRegistry>.Instance);
        var calendarRepo = Substitute.For<ICalendarRepository>();
        var syncStateRepo = Substitute.For<ISyncStateRepository>();
        var sinkProvider = Substitute.For<ICalendarProvider>();

        registry.Sinks.Returns(new List<SinkInfo> { new("acc-1", "cal-sink", sinkProvider) });

        var svc = new FreeBusySinkService(registry, calendarRepo, syncStateRepo,
            BuildPimConfig(), NullLogger<FreeBusySinkService>.Instance);

        return (svc, registry, calendarRepo, syncStateRepo, sinkProvider);
    }

    private static PimConfig BuildPimConfig() => new(
        [],
        new UiConfig("UTC", null),
        new SystemConfig(null, "open-meteo"),
        new StorageConfig("test.db", "/tmp/attach", 6, 6),
        new ServerConfig("127.0.0.1", 9400, 9401));
}
