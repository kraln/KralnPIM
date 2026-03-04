using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using PIM.Core.Data;
using PIM.Core.Models;

namespace PIM.Setup.Auth;

internal static class GoogleAuthFlow
{
    private static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/gmail.modify",
        "https://www.googleapis.com/auth/calendar.events",
        "https://www.googleapis.com/auth/calendar.readonly",
    ];

    public static async Task<bool> AuthorizeAsync(
        string clientId,
        string clientSecret,
        string accountId,
        IAuthRepository authRepo,
        Action<string> onStatus,
        CancellationToken ct)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
            },
            Scopes = Scopes,
            DataStore = new AuthRepositoryDataStore(authRepo, accountId),
        });

        // Check for existing valid token
        var existingToken = await flow.LoadTokenAsync(accountId, ct);
        if (existingToken is not null && !string.IsNullOrEmpty(existingToken.RefreshToken))
        {
            onStatus("Existing Google authorization found.");
            return true;
        }

        // Start listener BEFORE opening browser so callback port is ready
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}/";
        onStatus($"Listening on port {port} for OAuth callback...");

        var request = flow.CreateAuthorizationCodeRequest(redirectUri);
        request.Scope = string.Join(" ", Scopes);
        var authUrl = request.Build().ToString();

        if (TryOpenBrowser(authUrl))
        {
            onStatus("Browser opened — authorize the app, then return here.");
        }
        else
        {
            TryCopyToClipboard(authUrl);
            onStatus($"Open this URL to authorize:\n{authUrl}");
        }

        // Wait for the OAuth callback
        var code = await WaitForAuthCodeAsync(listener, ct);
        onStatus("Authorization code received, exchanging for token...");

        var tokenResponse = await flow.ExchangeCodeForTokenAsync(accountId, code, redirectUri, ct);

        await authRepo.SaveOAuthTokenAsync(new OAuthToken(
            AccountId: accountId,
            AccessToken: tokenResponse.AccessToken,
            RefreshToken: tokenResponse.RefreshToken ?? "",
            ExpiresAt: tokenResponse.IssuedUtc.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600)
        ), ct);

        onStatus("Google authorization successful.");
        return true;
    }

    private static async Task<string> WaitForAuthCodeAsync(TcpListener listener, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        await using var stream = client.GetStream();

        // Read the HTTP request
        var buffer = new byte[4096];
        var bytesRead = await stream.ReadAsync(buffer, ct);
        var requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Parse "GET /?code=...&scope=... HTTP/1.1"
        var firstLine = requestText.Split('\n')[0];
        var path = firstLine.Split(' ')[1]; // "/?code=...&scope=..."
        var query = HttpUtility.ParseQueryString(path.Contains('?') ? path[(path.IndexOf('?') + 1)..] : "");
        var code = query["code"]
            ?? throw new InvalidOperationException("No authorization code received from Google.");

        // Send success response
        const string body = "<html><body><h1>Authorization successful!</h1><p>You can close this window.</p></body></html>";
        var response = $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
        var responseBytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(responseBytes, ct);

        return code;
    }

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo("xdg-open", url)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var proc = Process.Start(psi);
            return proc is not null;
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static void TryCopyToClipboard(string text)
    {
        foreach (var (cmd, args) in new[] { ("wl-copy", text), ("xclip", "-selection clipboard") })
        {
            try
            {
                var psi = new ProcessStartInfo(cmd, args)
                {
                    RedirectStandardInput = cmd == "xclip",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                var proc = Process.Start(psi);
                if (proc is null) continue;
                if (cmd == "xclip")
                {
                    proc.StandardInput.Write(text);
                    proc.StandardInput.Close();
                }
                proc.WaitForExit(2000);
                return;
            }
            catch { /* try next */ }
        }
    }
}

internal sealed class AuthRepositoryDataStore(
    IAuthRepository authRepo, string accountId) : IDataStore
{
    public async Task StoreAsync<T>(string key, T value)
    {
        if (value is TokenResponse token)
        {
            await authRepo.SaveOAuthTokenAsync(new OAuthToken(
                AccountId: accountId,
                AccessToken: token.AccessToken,
                RefreshToken: token.RefreshToken ?? "",
                ExpiresAt: token.IssuedUtc.AddSeconds(token.ExpiresInSeconds ?? 3600)
            ));
        }
    }

    public async Task<T> GetAsync<T>(string key)
    {
        if (typeof(T) != typeof(TokenResponse))
            return default!;

        var oauthToken = await authRepo.GetOAuthTokenAsync(accountId);
        if (oauthToken is null)
            return default!;

        var response = new TokenResponse
        {
            AccessToken = oauthToken.AccessToken,
            RefreshToken = oauthToken.RefreshToken,
            ExpiresInSeconds = (long)(oauthToken.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds,
            IssuedUtc = DateTime.UtcNow,
        };
        return (T)(object)response;
    }

    public Task DeleteAsync<T>(string key) => Task.CompletedTask;

    public Task ClearAsync() => Task.CompletedTask;
}
