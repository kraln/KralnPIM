using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Identity.Client;
using PIM.Core;
using PIM.Core.Data;

namespace PIM.Setup.Auth;

internal static class CalendarDiscovery
{
    public static async Task<List<(string Id, string Name)>> DiscoverGoogleCalendarsAsync(
        IAuthRepository authRepo,
        string accountId,
        string clientId,
        string clientSecret,
        CancellationToken ct)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = ["https://www.googleapis.com/auth/calendar.readonly"],
            DataStore = new AuthRepositoryDataStore(authRepo, accountId),
        });

        var token = await flow.LoadTokenAsync(accountId, ct);
        if (token is null || string.IsNullOrEmpty(token.RefreshToken))
            throw new InvalidOperationException("No Google token available. Authenticate first.");

        if (token.IsStale)
            token = await flow.RefreshTokenAsync(accountId, token.RefreshToken, ct);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var json = await http.GetStringAsync(
            "https://www.googleapis.com/calendar/v3/users/me/calendarList", ct);

        using var doc = JsonDocument.Parse(json);
        var results = new List<(string Id, string Name)>();

        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                var summary = item.TryGetProperty("summary", out var s) ? s.GetString() : id;
                if (id is not null)
                    results.Add((id, summary ?? id));
            }
        }

        return results;
    }

    public static async Task<List<(string Id, string Name)>> DiscoverO365CalendarsAsync(
        IAuthRepository authRepo,
        string accountId,
        string clientId,
        string tenantId,
        CancellationToken ct)
    {
        var stored = await authRepo.GetOAuthTokenAsync(accountId, ct);
        if (stored is null || string.IsNullOrEmpty(stored.AccessToken))
            throw new InvalidOperationException("No O365 token available. Authenticate first.");

        var app = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .Build();

        app.UserTokenCache.SetBeforeAccessAsync(args =>
        {
            try
            {
                var bytes = Convert.FromBase64String(stored.AccessToken);
                args.TokenCache.DeserializeMsalV3(bytes);
            }
            catch { /* Not a valid MSAL cache blob */ }
            return Task.CompletedTask;
        });

        var accounts = await app.GetAccountsAsync();
        var msalAccount = accounts.FirstOrDefault()
            ?? throw new InvalidOperationException("No cached O365 account. Re-authenticate first.");

        var result = await app.AcquireTokenSilent(["Calendars.ReadWrite"], msalAccount).ExecuteAsync(ct);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);

        var json = await http.GetStringAsync(
            "https://graph.microsoft.com/v1.0/me/calendars", ct);

        using var doc = JsonDocument.Parse(json);
        var results = new List<(string Id, string Name)>();

        if (doc.RootElement.TryGetProperty("value", out var value))
        {
            foreach (var item in value.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : id;
                if (id is not null)
                    results.Add((id, name ?? id));
            }
        }

        return results;
    }

    public static async Task<List<(string Id, string Name, string Url)>> DiscoverCalDavCalendarsAsync(
        string serverUrl,
        string username,
        string password,
        bool ignoreSsl,
        CancellationToken ct)
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
        if (ignoreSsl)
            handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            };

        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");

        // Step 1: Try .well-known/caldav to find the DAV root
        var davRoot = baseUri;
        try
        {
            var wellKnown = new Uri(baseUri, ".well-known/caldav");
            var wkRequest = new HttpRequestMessage(new HttpMethod("PROPFIND"), wellKnown);
            wkRequest.Headers.Add("Depth", "0");
            wkRequest.Content = new StringContent(
                "<?xml version=\"1.0\"?><propfind xmlns=\"DAV:\"><prop><current-user-principal/></prop></propfind>",
                Encoding.UTF8, "application/xml");

            var wkResponse = await http.SendAsync(wkRequest, ct);
            if (wkResponse.Headers.Location is not null)
                davRoot = new Uri(baseUri, wkResponse.Headers.Location);
            else if (wkResponse.RequestMessage?.RequestUri is not null)
                davRoot = wkResponse.RequestMessage.RequestUri;
        }
        catch { /* fall through to use baseUri */ }

        // Step 2: Find current-user-principal
        var principalUrl = await FindPrincipalAsync(http, davRoot, ct) ?? davRoot;

        // Step 3: Find calendar-home-set
        var homeSetUrl = await FindCalendarHomeSetAsync(http, principalUrl, ct) ?? principalUrl;

        // Step 4: List calendars from the home set
        return await ListCalendarsAsync(http, homeSetUrl, ct);
    }

    private static readonly XNamespace DavNs = "DAV:";
    private static readonly XNamespace CalNs = "urn:ietf:params:xml:ns:caldav";

    private static async Task<Uri?> FindPrincipalAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        var body = new XElement(DavNs + "propfind",
            new XElement(DavNs + "prop",
                new XElement(DavNs + "current-user-principal")
            )
        ).ToString();

        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "0");

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 207)
            return null;

        var xml = await response.Content.ReadAsStringAsync(ct);
        var doc = XDocument.Parse(xml);

        var href = doc.Descendants(DavNs + "current-user-principal")
            .Descendants(DavNs + "href")
            .FirstOrDefault()?.Value;

        return href is not null ? new Uri(url, href) : null;
    }

    private static async Task<Uri?> FindCalendarHomeSetAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        var body = new XElement(DavNs + "propfind",
            new XElement(DavNs + "prop",
                new XElement(CalNs + "calendar-home-set")
            )
        ).ToString();

        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "0");

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 207)
            return null;

        var xml = await response.Content.ReadAsStringAsync(ct);
        var doc = XDocument.Parse(xml);

        var href = doc.Descendants(CalNs + "calendar-home-set")
            .Descendants(DavNs + "href")
            .FirstOrDefault()?.Value;

        return href is not null ? new Uri(url, href) : null;
    }

    private static async Task<List<(string Id, string Name, string Url)>> ListCalendarsAsync(
        HttpClient http, Uri homeSetUrl, CancellationToken ct)
    {
        var body = new XElement(DavNs + "propfind",
            new XElement(DavNs + "prop",
                new XElement(DavNs + "resourcetype"),
                new XElement(DavNs + "displayname")
            )
        ).ToString();

        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), homeSetUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "1");

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 207)
            return [];

        var xml = await response.Content.ReadAsStringAsync(ct);
        var doc = XDocument.Parse(xml);

        var results = new List<(string Id, string Name, string Url)>();

        foreach (var responseEl in doc.Descendants(DavNs + "response"))
        {
            var resourceType = responseEl.Descendants(DavNs + "resourcetype").FirstOrDefault();
            if (resourceType is null) continue;

            // Must have <calendar/> in resourcetype
            var isCalendar = resourceType.Descendants(CalNs + "calendar").Any();
            if (!isCalendar) continue;

            var href = responseEl.Element(DavNs + "href")?.Value;
            if (href is null) continue;

            var displayName = responseEl.Descendants(DavNs + "displayname").FirstOrDefault()?.Value;
            var fullUrl = new Uri(homeSetUrl, href).ToString();

            // Derive a short ID from the last path segment
            var segments = href.TrimEnd('/').Split('/');
            var id = segments.Length > 0 ? segments[^1] : href;

            results.Add((id, displayName ?? id, fullUrl));
        }

        return results;
    }
}
