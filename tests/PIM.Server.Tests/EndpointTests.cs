using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Core.Models;
using PIM.Core.Providers;
using PIM.Core.Serialization;
using PIM.Server.Models;
using PIM.Server.Registration;
using PIM.Server.Services;
using PIM.Server.WebSocket;

namespace PIM.Server.Tests;

/// <summary>
/// Integration tests for REST API endpoints using a real in-memory test server.
/// </summary>
public class EndpointTests : IAsyncLifetime
{
    private readonly IEmailRepository _emailRepo = Substitute.For<IEmailRepository>();
    private readonly ICalendarRepository _calendarRepo = Substitute.For<ICalendarRepository>();
    private readonly ISearchService _searchService = Substitute.For<ISearchService>();
    private readonly IPowerInfoProvider _powerProvider = Substitute.For<IPowerInfoProvider>();
    private readonly IWeatherProvider _weatherProvider = Substitute.For<IWeatherProvider>();
    private readonly IClockProvider _clockProvider = Substitute.For<IClockProvider>();
    private readonly ProviderRegistry _registry;
    private readonly AccountStatusTracker _statusTracker = new();
    private readonly WebSocketBroadcaster _broadcaster;

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private static readonly PimConfig TestConfig = new(
        Accounts: [
            new AccountConfig("acc-1", AccountType.Google, "Test Account",
                null, null, null, null, null, null, "cid", "csecret", null, null)
        ],
        Ui: new UiConfig("America/New_York", "Europe/London"),
        System: new SystemConfig("40.7,-74.0", "open-meteo"),
        Storage: new StorageConfig("test.db", "/tmp/attach", 6, 6),
        Server: new ServerConfig("127.0.0.1", 0, 0));

    public EndpointTests()
    {
        _registry = Substitute.For<ProviderRegistry>(NullLogger<ProviderRegistry>.Instance);
        _registry.AccountIds.Returns(["acc-1"]);
        _broadcaster = new WebSocketBroadcaster(NullLogger<WebSocketBroadcaster>.Instance);
        _emailRepo.GetAccountCountsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, (int Unread, int Flagged)>());
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Services.AddSingleton(TestConfig);
        builder.Services.AddSingleton(_emailRepo);
        builder.Services.AddSingleton(_calendarRepo);
        builder.Services.AddSingleton<ISearchService>(_searchService);
        builder.Services.AddSingleton(_registry);
        builder.Services.AddSingleton<IPowerInfoProvider>(_powerProvider);
        builder.Services.AddSingleton<IWeatherProvider>(_weatherProvider);
        builder.Services.AddSingleton<IClockProvider>(_clockProvider);
        builder.Services.AddSingleton(_statusTracker);
        builder.Services.AddSingleton(_broadcaster);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, PimJsonContext.Default);
            options.SerializerOptions.TypeInfoResolverChain.Insert(1, ServerJsonContext.Default);
        });

        // Use a random port
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();

        _app.MapGet("/api/health", () => Results.Ok("ok"));
        Api.MailEndpoints.MapMailEndpoints(_app);
        Api.CalendarEndpoints.MapCalendarEndpoints(_app);
        Api.SearchEndpoints.MapSearchEndpoints(_app);
        Api.SystemEndpoints.MapSystemEndpoints(_app);

        await _app.StartAsync();

        var address = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // --- Health ---

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Mail ---

    [Fact]
    public async Task GetMail_ReturnsListFromRepo()
    {
        var headers = new List<EmailHeader>
        {
            new("msg-1", "acc-1", "INBOX", "Test Subject", "a@b.com", "Alice",
                ["b@c.com"], [], DateTimeOffset.UtcNow, false, false, "snippet", [])
        };
        _emailRepo.ListAsync(Arg.Any<EmailListQuery>(), Arg.Any<CancellationToken>())
            .Returns(headers);

        var response = await _client.GetAsync("/api/mail");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("msg-1", json);
        Assert.Contains("Test Subject", json);
    }

    [Fact]
    public async Task GetMailById_NotFound_Returns404()
    {
        _emailRepo.GetHeaderAsync("nonexistent", Arg.Any<CancellationToken>())
            .Returns((EmailHeader?)null);

        var response = await _client.GetAsync("/api/mail/nonexistent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMailById_ReturnsHeaderAndBody()
    {
        var header = new EmailHeader("msg-1", "acc-1", "INBOX", "Test", "a@b.com", "Alice",
            ["b@c.com"], [], DateTimeOffset.UtcNow, false, false, null, []);
        var body = new EmailBody("msg-1", "Hello world");

        _emailRepo.GetHeaderAsync("msg-1", Arg.Any<CancellationToken>()).Returns(header);
        _emailRepo.GetBodyAsync("msg-1", Arg.Any<CancellationToken>()).Returns(body);

        var response = await _client.GetAsync("/api/mail/msg-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hello world", json);
    }

    [Fact]
    public async Task SendMail_OfflineAccount_Returns503()
    {
        _statusTracker.MarkOffline("acc-1");

        var email = new OutboundEmail("acc-1", ["b@c.com"], [], [], "Subject", "Body", null);
        var response = await _client.PostAsJsonAsync("/api/mail/send", email);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_ReturnsList()
    {
        _statusTracker.MarkOnline("acc-1");

        var response = await _client.GetAsync("/api/mail/accounts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("acc-1", json);
        Assert.Contains("Test Account", json);
    }

    // --- Calendar ---

    [Fact]
    public async Task GetCalendarEvents_ReturnsFromRepo()
    {
        var events = new List<CalendarEvent>
        {
            new("evt-1", "acc-1", "cal-1", "Team Meeting", null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
                false, "Room A", [], null, EventStatus.Confirmed)
        };
        _calendarRepo.GetEventsInRangeAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(events);

        var response = await _client.GetAsync("/api/calendar/events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Team Meeting", json);
    }

    [Fact]
    public async Task CreateEvent_Offline_Returns503()
    {
        _statusTracker.MarkOffline("acc-1");

        var evt = new CalendarEvent("evt-new", "acc-1", "cal-1", "New Event", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            false, null, [], null, EventStatus.Confirmed);

        var response = await _client.PostAsJsonAsync("/api/calendar/events", evt);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // --- Search ---

    [Fact]
    public async Task LocalSearch_ReturnsResults()
    {
        var searchResult = new SearchResult(
            [new EmailHeader("msg-1", "acc-1", "INBOX", "Revenue Report", "a@b.com", "Alice",
                ["b@c.com"], [], DateTimeOffset.UtcNow, false, false, null, [])],
            []);

        _searchService.LocalSearchAsync("revenue", Arg.Any<SearchScope>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(searchResult);

        var response = await _client.GetAsync("/api/search?q=revenue");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Revenue Report", json);
    }

    [Fact]
    public async Task DeepSearch_EmptyQuery_Returns400()
    {
        var request = new DeepSearchRequest("", null);
        var response = await _client.PostAsJsonAsync("/api/search/deep", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- System ---

    [Fact]
    public async Task GetPower_ReturnsInfo()
    {
        _powerProvider.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new PowerInfo(75, "2:30", 12.5));

        var response = await _client.GetAsync("/api/system/power");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("75", json);
    }

    [Fact]
    public async Task GetWeather_ReturnsInfo()
    {
        _weatherProvider.GetCurrentAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new WeatherInfo(22.5, "Partly Cloudy", 65, 12.3));

        var response = await _client.GetAsync("/api/system/weather");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Partly Cloudy", json);
    }

    [Fact]
    public async Task GetClock_ReturnsZones()
    {
        _clockProvider.GetCurrent(Arg.Any<List<string>>())
            .Returns(new ClockInfo([
                new TimeZoneDisplay("America/New_York", "Eastern", DateTimeOffset.UtcNow)
            ]));

        var response = await _client.GetAsync("/api/system/clock");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        // Label is overridden with config timezone ID
        Assert.Contains("America/New_York", json);
    }

    [Fact]
    public async Task GetStatus_ReturnsAccountInfo()
    {
        _statusTracker.MarkOnline("acc-1");

        var response = await _client.GetAsync("/api/system/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("acc-1", json);
    }
}
