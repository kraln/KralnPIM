using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PIM.Sync.CalDav;

namespace PIM.Sync.CalDav.Tests;

public class CalDavClientTests
{
    private const string CalendarUrl = "https://dav.example.com/calendars/user/default/";

    [Fact]
    public void ParseEtagResponse_ValidXml_ReturnsEtags()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <d:multistatus xmlns:d="DAV:">
              <d:response>
                <d:href>/calendars/user/default/</d:href>
                <d:propstat>
                  <d:prop>
                    <d:getetag>"collection-etag"</d:getetag>
                  </d:prop>
                  <d:status>HTTP/1.1 200 OK</d:status>
                </d:propstat>
              </d:response>
              <d:response>
                <d:href>/calendars/user/default/event1.ics</d:href>
                <d:propstat>
                  <d:prop>
                    <d:getetag>"etag-1"</d:getetag>
                  </d:prop>
                  <d:status>HTTP/1.1 200 OK</d:status>
                </d:propstat>
              </d:response>
              <d:response>
                <d:href>/calendars/user/default/event2.ics</d:href>
                <d:propstat>
                  <d:prop>
                    <d:getetag>"etag-2"</d:getetag>
                  </d:prop>
                  <d:status>HTTP/1.1 200 OK</d:status>
                </d:propstat>
              </d:response>
            </d:multistatus>
            """;

        var result = CalDavClient.ParseEtagResponse(xml);

        Assert.Equal(2, result.Count);
        Assert.Equal("etag-1", result["/calendars/user/default/event1.ics"]);
        Assert.Equal("etag-2", result["/calendars/user/default/event2.ics"]);
    }

    [Fact]
    public void ParseEtagResponse_SkipsNonIcsResources()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <d:multistatus xmlns:d="DAV:">
              <d:response>
                <d:href>/calendars/user/default/</d:href>
                <d:propstat>
                  <d:prop>
                    <d:getetag>"collection"</d:getetag>
                  </d:prop>
                </d:propstat>
              </d:response>
              <d:response>
                <d:href>/calendars/user/default/event1.ics</d:href>
                <d:propstat>
                  <d:prop>
                    <d:getetag>"etag-1"</d:getetag>
                  </d:prop>
                </d:propstat>
              </d:response>
            </d:multistatus>
            """;

        var result = CalDavClient.ParseEtagResponse(xml);

        Assert.Single(result);
        Assert.True(result.ContainsKey("/calendars/user/default/event1.ics"));
    }

    [Fact]
    public void ParseEtagResponse_EmptyMultistatus_ReturnsEmpty()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <d:multistatus xmlns:d="DAV:">
            </d:multistatus>
            """;

        var result = CalDavClient.ParseEtagResponse(xml);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseCalendarDataResponse_ValidXml_ReturnsEventData()
    {
        var icsData = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:evt1\nEND:VEVENT\nEND:VCALENDAR";

        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <d:multistatus xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav">
              <d:response>
                <d:href>/calendars/user/default/event1.ics</d:href>
                <d:propstat>
                  <d:prop>
                    <d:getetag>"etag-1"</d:getetag>
                    <cal:calendar-data>{icsData}</cal:calendar-data>
                  </d:prop>
                  <d:status>HTTP/1.1 200 OK</d:status>
                </d:propstat>
              </d:response>
            </d:multistatus>
            """;

        var result = CalDavClient.ParseCalendarDataResponse(xml);

        Assert.Single(result);
        Assert.Contains("BEGIN:VCALENDAR", result["/calendars/user/default/event1.ics"]);
        Assert.Contains("UID:evt1", result["/calendars/user/default/event1.ics"]);
    }

    [Fact]
    public void ParseCalendarDataResponse_MultipleEvents_ReturnsAll()
    {
        var ics1 = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:evt1\nEND:VEVENT\nEND:VCALENDAR";
        var ics2 = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:evt2\nEND:VEVENT\nEND:VCALENDAR";

        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <d:multistatus xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav">
              <d:response>
                <d:href>/calendars/user/default/event1.ics</d:href>
                <d:propstat>
                  <d:prop>
                    <cal:calendar-data>{ics1}</cal:calendar-data>
                  </d:prop>
                </d:propstat>
              </d:response>
              <d:response>
                <d:href>/calendars/user/default/event2.ics</d:href>
                <d:propstat>
                  <d:prop>
                    <cal:calendar-data>{ics2}</cal:calendar-data>
                  </d:prop>
                </d:propstat>
              </d:response>
            </d:multistatus>
            """;

        var result = CalDavClient.ParseCalendarDataResponse(xml);

        Assert.Equal(2, result.Count);
        Assert.Contains("evt1", result["/calendars/user/default/event1.ics"]);
        Assert.Contains("evt2", result["/calendars/user/default/event2.ics"]);
    }

    [Fact]
    public async Task GetCtagAsync_SendsPropfindWithDepthZero()
    {
        var responseXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <d:multistatus xmlns:d="DAV:" xmlns:cs="http://calendarserver.org/ns/">
              <d:response>
                <d:href>/calendars/user/default/</d:href>
                <d:propstat>
                  <d:prop>
                    <cs:getctag>ctag-abc123</cs:getctag>
                  </d:prop>
                  <d:status>HTTP/1.1 200 OK</d:status>
                </d:propstat>
              </d:response>
            </d:multistatus>
            """;

        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpHandler((req, _) =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml, Encoding.UTF8, "application/xml"),
            });
        });

        var httpClient = new HttpClient(handler);
        var client = new CalDavClient(httpClient, CalendarUrl, "user", "pass", NullLogger.Instance);

        var ctag = await client.GetCtagAsync(CancellationToken.None);

        Assert.Equal("ctag-abc123", ctag);
        Assert.NotNull(capturedRequest);
        Assert.Equal("PROPFIND", capturedRequest!.Method.Method);
        Assert.Equal("0", capturedRequest.Headers.GetValues("Depth").First());
        Assert.NotNull(capturedRequest.Headers.Authorization);
        Assert.Equal("Basic", capturedRequest.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task GetEtagsAsync_SendsPropfindWithDepthOne()
    {
        var responseXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <d:multistatus xmlns:d="DAV:">
              <d:response>
                <d:href>/calendars/user/default/event1.ics</d:href>
                <d:propstat>
                  <d:prop>
                    <d:getetag>"etag-1"</d:getetag>
                  </d:prop>
                </d:propstat>
              </d:response>
            </d:multistatus>
            """;

        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpHandler((req, _) =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml, Encoding.UTF8, "application/xml"),
            });
        });

        var httpClient = new HttpClient(handler);
        var client = new CalDavClient(httpClient, CalendarUrl, "user", "pass", NullLogger.Instance);

        var etags = await client.GetEtagsAsync(CancellationToken.None);

        Assert.Single(etags);
        Assert.Equal("etag-1", etags["/calendars/user/default/event1.ics"]);
        Assert.Equal("PROPFIND", capturedRequest!.Method.Method);
        Assert.Equal("1", capturedRequest.Headers.GetValues("Depth").First());
    }

    [Fact]
    public async Task GetEventsAsync_SendsReportWithHrefs()
    {
        var icsData = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:evt1\nEND:VEVENT\nEND:VCALENDAR";
        var responseXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <d:multistatus xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav">
              <d:response>
                <d:href>/calendars/user/default/event1.ics</d:href>
                <d:propstat>
                  <d:prop>
                    <cal:calendar-data>{icsData}</cal:calendar-data>
                  </d:prop>
                </d:propstat>
              </d:response>
            </d:multistatus>
            """;

        string? capturedBody = null;
        var handler = new MockHttpHandler(async (req, _) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml, Encoding.UTF8, "application/xml"),
            };
        });

        var httpClient = new HttpClient(handler);
        var client = new CalDavClient(httpClient, CalendarUrl, "user", "pass", NullLogger.Instance);

        var result = await client.GetEventsAsync(["/calendars/user/default/event1.ics"], CancellationToken.None);

        Assert.Single(result);
        Assert.NotNull(capturedBody);
        Assert.Contains("calendar-multiget", capturedBody);
        Assert.Contains("/calendars/user/default/event1.ics", capturedBody);
    }

    [Fact]
    public async Task PutEventAsync_SendsPutWithICalContent()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new MockHttpHandler(async (req, _) =>
        {
            capturedRequest = req;
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        var httpClient = new HttpClient(handler);
        var client = new CalDavClient(httpClient, CalendarUrl, "user", "pass", NullLogger.Instance);

        var icsData = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:new-event\r\nEND:VEVENT\r\nEND:VCALENDAR";
        await client.PutEventAsync("new-event.ics", icsData, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Put, capturedRequest!.Method);
        Assert.EndsWith("new-event.ics", capturedRequest.RequestUri!.ToString());
        Assert.Equal(icsData, capturedBody);
        Assert.Equal("text/calendar", capturedRequest.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task DeleteEventAsync_SendsDeleteRequest()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpHandler((req, _) =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        var httpClient = new HttpClient(handler);
        var client = new CalDavClient(httpClient, CalendarUrl, "user", "pass", NullLogger.Instance);

        await client.DeleteEventAsync("old-event.ics", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Delete, capturedRequest!.Method);
        Assert.EndsWith("old-event.ics", capturedRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetAllEventsAsync_SendsCalendarQueryWithTimeRange()
    {
        var responseXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <d:multistatus xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav">
            </d:multistatus>
            """;

        string? capturedBody = null;
        var handler = new MockHttpHandler(async (req, _) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml, Encoding.UTF8, "application/xml"),
            };
        });

        var httpClient = new HttpClient(handler);
        var client = new CalDavClient(httpClient, CalendarUrl, "user", "pass", NullLogger.Instance);

        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);
        await client.GetAllEventsAsync(start, end, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("calendar-query", capturedBody);
        Assert.Contains("time-range", capturedBody);
        Assert.Contains("20240101T000000Z", capturedBody);
        Assert.Contains("20241231T235959Z", capturedBody);
    }

    [Fact]
    public void Constructor_SetsBasicAuthHeader()
    {
        var httpClient = new HttpClient(new MockHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        _ = new CalDavClient(httpClient, CalendarUrl, "testuser", "testpass", NullLogger.Instance);

        var auth = httpClient.DefaultRequestHeaders.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Basic", auth!.Scheme);

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!));
        Assert.Equal("testuser:testpass", decoded);
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
