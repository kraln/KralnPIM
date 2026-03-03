using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

        // Allocate a random loopback port for the OAuth callback
        var port = GetAvailablePort();
        var redirectUri = $"http://127.0.0.1:{port}/";

        var authUrl = flow.CreateAuthorizationCodeRequest(redirectUri).Build().ToString();
        onStatus($"Open this URL to authorize:\n{authUrl}");

        // Launch the browser
        try
        {
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch
        {
            // Browser launch may fail in headless environments; URL was already reported via onStatus
        }

        // Wait for the OAuth callback
        var code = await ListenForAuthCodeAsync(redirectUri, ct);

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

    private static async Task<string> ListenForAuthCodeAsync(string redirectUri, CancellationToken ct)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var context = await listener.GetContextAsync().WaitAsync(ct);
        var code = context.Request.QueryString["code"]
            ?? throw new InvalidOperationException("No authorization code received from Google.");

        var responseBytes = Encoding.UTF8.GetBytes(
            "<html><body><h1>Authorization successful!</h1><p>You can close this window.</p></body></html>");
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = responseBytes.Length;
        await context.Response.OutputStream.WriteAsync(responseBytes, ct);
        context.Response.Close();

        return code;
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
