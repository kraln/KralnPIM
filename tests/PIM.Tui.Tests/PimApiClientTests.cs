using System.Net;
using System.Text.Json;
using PIM.Core.Models;
using PIM.Core.Serialization;
using PIM.Tui.Client;
using PIM.Tui.Models;

namespace PIM.Tui.Tests;

public sealed class PimApiClientTests : IDisposable
{
    private readonly JsonSerializerOptions _json;

    public PimApiClientTests()
    {
        _json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _json.TypeInfoResolverChain.Add(PimJsonContext.Default);
        _json.TypeInfoResolverChain.Add(TuiJsonContext.Default);
    }

    public void Dispose() { }

    private static PimApiClient CreateClient(HttpStatusCode status, string json)
    {
        var handler = new FakeHandler(status, json);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9400") };
        return new PimApiClient(http);
    }

    [Fact]
    public async Task ListMailAsync_BuildsCorrectUrl()
    {
        var headers = new List<EmailHeader>
        {
            new("msg-1", "acct-1", "INBOX", "Subject", "from@test.com", "From",
                ["to@test.com"], [], DateTimeOffset.UtcNow, false, false, "snippet", [])
        };

        using var client = CreateClient(HttpStatusCode.OK, JsonSerializer.Serialize(headers, _json));
        var result = await client.ListMailAsync(accountId: "acct-1", isRead: false, offset: 10, limit: 25);

        Assert.Single(result);
        Assert.Equal("msg-1", result[0].MessageId);
    }

    [Fact]
    public async Task GetMailDetailAsync_ReturnsMailDetail()
    {
        var header = new EmailHeader("msg-1", "acct-1", "INBOX", "Test", "from@test.com", "From",
            ["to@test.com"], [], DateTimeOffset.UtcNow, true, false, null, []);
        var detail = new MailDetail(header, "Body text");

        using var client = CreateClient(HttpStatusCode.OK, JsonSerializer.Serialize(detail, _json));
        var result = await client.GetMailDetailAsync("msg-1");

        Assert.NotNull(result);
        Assert.Equal("Body text", result.PlainTextBody);
        Assert.Equal("msg-1", result.Header.MessageId);
    }

    [Fact]
    public async Task GetAccountsAsync_ReturnsAccounts()
    {
        var accounts = new List<AccountOverview>
        {
            new("acct-1", "Personal", "Imap", true, 3, 1, null)
        };

        using var client = CreateClient(HttpStatusCode.OK, JsonSerializer.Serialize(accounts, _json));
        var result = await client.GetAccountsAsync();

        Assert.Single(result);
        Assert.Equal("Personal", result[0].DisplayName);
        Assert.True(result[0].Online);
    }

    [Fact]
    public async Task GetEventsAsync_ReturnsEvents()
    {
        var events = new List<CalendarEvent>
        {
            new("evt-1", "acct-1", "cal-1", "Standup", null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
                false, null, [], null, EventStatus.Confirmed)
        };

        using var client = CreateClient(HttpStatusCode.OK, JsonSerializer.Serialize(events, _json));
        var result = await client.GetEventsAsync(DateTimeOffset.UtcNow.Date, DateTimeOffset.UtcNow.Date.AddDays(1));

        Assert.Single(result);
        Assert.Equal("Standup", result[0].Summary);
    }

    [Fact]
    public async Task SearchLocalAsync_ReturnsSearchResult()
    {
        var searchResult = new SearchResult(
            [new("msg-1", "acct-1", "INBOX", "Test", "a@b.com", "A",
                ["b@c.com"], [], DateTimeOffset.UtcNow, false, false, null, [])],
            []);

        using var client = CreateClient(HttpStatusCode.OK, JsonSerializer.Serialize(searchResult, _json));
        var result = await client.SearchLocalAsync("test query", "mail");

        Assert.NotNull(result);
        Assert.Single(result.Emails);
    }

    [Fact]
    public async Task GetPowerAsync_ReturnsPowerInfo()
    {
        var power = new PowerInfo(87, "3h 12m", 8.5);

        using var client = CreateClient(HttpStatusCode.OK, JsonSerializer.Serialize(power, _json));
        var result = await client.GetPowerAsync();

        Assert.NotNull(result);
        Assert.Equal(87, result.BatteryPercent);
    }

    [Fact]
    public async Task GetMailDetailAsync_ReturnsNullOnNotFound()
    {
        using var client = CreateClient(HttpStatusCode.NotFound,
            JsonSerializer.Serialize(new ErrorResponse("Not found"), _json));

        var result = await client.GetMailDetailAsync("no-such-msg");
        Assert.Null(result);
    }

    [Fact]
    public async Task SendMailAsync_ThrowsOn503()
    {
        using var client = CreateClient(HttpStatusCode.ServiceUnavailable,
            JsonSerializer.Serialize(new ErrorResponse("Account offline"), _json));

        var email = new OutboundEmail("acct-1", ["to@test.com"], [], [], "Subject", "Body", null);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendMailAsync(email));
    }

    [Fact]
    public async Task DownloadAttachmentAsync_ReturnsFilePath()
    {
        var result = new AttachmentDownloadResult("/tmp/file.pdf");

        using var client = CreateClient(HttpStatusCode.OK, JsonSerializer.Serialize(result, _json));
        var download = await client.DownloadAttachmentAsync("msg-1", "file.pdf");

        Assert.NotNull(download);
        Assert.Equal("/tmp/file.pdf", download.FilePath);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsSystemStatus()
    {
        var status = new SystemStatus([
            new AccountStatusInfo("acct-1", "Personal", true, null, DateTimeOffset.UtcNow)
        ]);

        using var client = CreateClient(HttpStatusCode.OK, JsonSerializer.Serialize(status, _json));
        var result = await client.GetStatusAsync();

        Assert.NotNull(result);
        Assert.Single(result.Accounts);
        Assert.True(result.Accounts[0].Online);
    }

    private sealed class FakeHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
