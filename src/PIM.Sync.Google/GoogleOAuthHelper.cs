using System.Net;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using PIM.Core.Data;
using PIM.Core.Models;

namespace PIM.Sync.Google;

public static class GoogleOAuthHelper
{
    private static readonly string[] Scopes =
    [
        GmailService.Scope.GmailModify,
        CalendarService.Scope.CalendarEvents,
        CalendarService.Scope.CalendarReadonly,
    ];

    public static async Task<UserCredential> AuthorizeAsync(
        string clientId,
        string clientSecret,
        string accountId,
        IAuthRepository authRepo,
        ILogger logger,
        CancellationToken ct,
        Action<string>? onAuthUrl = null)
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

        // Check for existing token
        var existingToken = await flow.LoadTokenAsync(accountId, ct);
        if (existingToken is not null && !string.IsNullOrEmpty(existingToken.RefreshToken))
        {
            return new UserCredential(flow, accountId, existingToken);
        }

        // Start loopback listener for OAuth callback
        var port = GetAvailablePort();
        var redirectUri = $"http://127.0.0.1:{port}/";

        var authUri = flow.CreateAuthorizationCodeRequest(redirectUri).Build();
        // Google's library leaves scopes space-separated in the query string;
        // replace literal spaces with %20 so the URL works in browsers/terminals.
        var authUrl = authUri.ToString().Replace(" ", "%20");
        logger.LogInformation("Open this URL in your browser to authorize:\n{AuthUrl}", authUrl);
        onAuthUrl?.Invoke(authUrl);

        var code = await ListenForAuthCodeAsync(redirectUri, ct);

        var tokenResponse = await flow.ExchangeCodeForTokenAsync(
            accountId, code, redirectUri, ct);

        // Persist to auth repository
        await authRepo.SaveOAuthTokenAsync(new OAuthToken(
            AccountId: accountId,
            AccessToken: tokenResponse.AccessToken,
            RefreshToken: tokenResponse.RefreshToken ?? "",
            ExpiresAt: tokenResponse.IssuedUtc.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600)
        ), ct);

        return new UserCredential(flow, accountId, tokenResponse);
    }

    private static async Task<string> ListenForAuthCodeAsync(
        string redirectUri, CancellationToken ct)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var context = await listener.GetContextAsync().WaitAsync(ct);
        var code = context.Request.QueryString["code"]
            ?? throw new InvalidOperationException("No authorization code received.");

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
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
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
