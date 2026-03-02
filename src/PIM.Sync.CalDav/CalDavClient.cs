using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace PIM.Sync.CalDav;

public sealed class CalDavClient
{
    private static readonly XNamespace DavNs = "DAV:";
    private static readonly XNamespace CalNs = "urn:ietf:params:xml:ns:caldav";
    private static readonly XNamespace CsNs = "http://calendarserver.org/ns/";

    private readonly HttpClient _httpClient;
    private readonly Uri _calendarUrl;
    private readonly ILogger _logger;

    public CalDavClient(HttpClient httpClient, string calendarUrl, string username, string password, ILogger logger)
    {
        _httpClient = httpClient;
        _calendarUrl = new Uri(calendarUrl.TrimEnd('/') + "/");
        _logger = logger;

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<string?> GetCtagAsync(CancellationToken ct)
    {
        var body = new XElement(DavNs + "propfind",
            new XElement(DavNs + "prop",
                new XElement(CsNs + "getctag")
            )
        ).ToString();

        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), _calendarUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "0");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(ct);
        var doc = XDocument.Parse(xml);

        var ctag = doc.Descendants(CsNs + "getctag").FirstOrDefault()?.Value;
        _logger.LogDebug("Got ctag: {Ctag}", ctag);
        return ctag;
    }

    public async Task<Dictionary<string, string>> GetEtagsAsync(CancellationToken ct)
    {
        var body = new XElement(DavNs + "propfind",
            new XElement(DavNs + "prop",
                new XElement(DavNs + "getetag")
            )
        ).ToString();

        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), _calendarUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "1");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(ct);
        return ParseEtagResponse(xml);
    }

    public async Task<Dictionary<string, string>> GetEventsAsync(IEnumerable<string> hrefs, CancellationToken ct)
    {
        var hrefElements = hrefs.Select(h => new XElement(DavNs + "href", h));

        var body = new XElement(CalNs + "calendar-multiget",
            new XElement(DavNs + "prop",
                new XElement(DavNs + "getetag"),
                new XElement(CalNs + "calendar-data")
            ),
            hrefElements
        ).ToString();

        var request = new HttpRequestMessage(new HttpMethod("REPORT"), _calendarUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "1");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(ct);
        return ParseCalendarDataResponse(xml);
    }

    public async Task<Dictionary<string, string>> GetAllEventsAsync(
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        var body = new XElement(CalNs + "calendar-query",
            new XElement(DavNs + "prop",
                new XElement(DavNs + "getetag"),
                new XElement(CalNs + "calendar-data")
            ),
            new XElement(CalNs + "filter",
                new XElement(CalNs + "comp-filter",
                    new XAttribute("name", "VCALENDAR"),
                    new XElement(CalNs + "comp-filter",
                        new XAttribute("name", "VEVENT"),
                        new XElement(CalNs + "time-range",
                            new XAttribute("start", rangeStart.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'")),
                            new XAttribute("end", rangeEnd.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'"))
                        )
                    )
                )
            )
        ).ToString();

        var request = new HttpRequestMessage(new HttpMethod("REPORT"), _calendarUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "1");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(ct);
        return ParseCalendarDataResponse(xml);
    }

    public async Task PutEventAsync(string href, string icalData, CancellationToken ct)
    {
        var url = new Uri(_calendarUrl, href);
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(icalData, Encoding.UTF8, "text/calendar"),
        };

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("PUT event to {Href}", href);
    }

    public async Task DeleteEventAsync(string href, CancellationToken ct)
    {
        var url = new Uri(_calendarUrl, href);
        var request = new HttpRequestMessage(HttpMethod.Delete, url);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("DELETE event at {Href}", href);
    }

    internal static Dictionary<string, string> ParseEtagResponse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var result = new Dictionary<string, string>();

        foreach (var responseEl in doc.Descendants(DavNs + "response"))
        {
            var href = responseEl.Element(DavNs + "href")?.Value;
            var etag = responseEl.Descendants(DavNs + "getetag").FirstOrDefault()?.Value;

            if (href is not null && etag is not null && href.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
            {
                result[href] = etag.Trim('"');
            }
        }

        return result;
    }

    internal static Dictionary<string, string> ParseCalendarDataResponse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var result = new Dictionary<string, string>();

        foreach (var responseEl in doc.Descendants(DavNs + "response"))
        {
            var href = responseEl.Element(DavNs + "href")?.Value;
            var calData = responseEl.Descendants(CalNs + "calendar-data").FirstOrDefault()?.Value;

            if (href is not null && calData is not null)
            {
                result[href] = calData;
            }
        }

        return result;
    }
}
