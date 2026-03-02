using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using PIM.Core.Data;
using PIM.Core.Models;

namespace PIM.Sync.Graph;

public sealed class GraphAuthProvider
{
    private static readonly string[] Scopes =
    [
        "Mail.ReadWrite",
        "Mail.Send",
        "Calendars.ReadWrite",
    ];

    private readonly string _accountId;
    private readonly string _clientId;
    private readonly string _tenantId;
    private readonly IAuthRepository _authRepo;
    private readonly ILogger _logger;
    private IPublicClientApplication? _msalApp;
    private string? _cachedAccessToken;
    private DateTimeOffset _tokenExpiry;

    public GraphAuthProvider(
        string accountId,
        string clientId,
        string tenantId,
        IAuthRepository authRepo,
        ILogger logger)
    {
        _accountId = accountId;
        _clientId = clientId;
        _tenantId = tenantId;
        _authRepo = authRepo;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            return _cachedAccessToken;

        var app = EnsureMsalApp();

        // Try silent acquisition first (uses MSAL in-memory + serialized cache)
        var accounts = await app.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        if (account is not null)
        {
            try
            {
                var silentResult = await app.AcquireTokenSilent(Scopes, account)
                    .ExecuteAsync(ct);
                return CacheToken(silentResult);
            }
            catch (MsalUiRequiredException)
            {
                // Token expired and refresh failed — fall through to device code
            }
        }

        // No cached account — try restoring from IAuthRepository
        var stored = await _authRepo.GetOAuthTokenAsync(_accountId, ct);
        if (stored is not null && !string.IsNullOrEmpty(stored.RefreshToken))
        {
            // MSAL cache was deserialized in SetBeforeAccessAsync, retry silent
            accounts = await app.GetAccountsAsync();
            account = accounts.FirstOrDefault();
            if (account is not null)
            {
                try
                {
                    var silentResult = await app.AcquireTokenSilent(Scopes, account)
                        .ExecuteAsync(ct);
                    return CacheToken(silentResult);
                }
                catch (MsalUiRequiredException)
                {
                    // Fall through to device code
                }
            }
        }

        // Interactive: device code flow
        var result = await app.AcquireTokenWithDeviceCode(Scopes, callback =>
        {
            _logger.LogInformation("To sign in, open {Url} and enter code {Code}",
                callback.VerificationUrl, callback.UserCode);
            return Task.CompletedTask;
        }).ExecuteAsync(ct);

        // Persist token to IAuthRepository
        await _authRepo.SaveOAuthTokenAsync(new OAuthToken(
            AccountId: _accountId,
            AccessToken: result.AccessToken,
            RefreshToken: "", // MSAL manages refresh tokens internally
            ExpiresAt: result.ExpiresOn
        ), ct);

        return CacheToken(result);
    }

    private IPublicClientApplication EnsureMsalApp()
    {
        if (_msalApp is not null)
            return _msalApp;

        _msalApp = PublicClientApplicationBuilder
            .Create(_clientId)
            .WithAuthority($"https://login.microsoftonline.com/{_tenantId}")
            .Build();

        // Wire up token cache serialization to IAuthRepository
        _msalApp.UserTokenCache.SetBeforeAccessAsync(async args =>
        {
            var stored = await _authRepo.GetOAuthTokenAsync(_accountId);
            if (stored is not null && !string.IsNullOrEmpty(stored.AccessToken))
            {
                // MSAL cache blob is stored in the AccessToken field as base64
                // when we serialize it in AfterAccess
                try
                {
                    var bytes = Convert.FromBase64String(stored.AccessToken);
                    args.TokenCache.DeserializeMsalV3(bytes);
                }
                catch (FormatException)
                {
                    // Not a valid cache blob — first time or legacy token
                }
            }
        });

        _msalApp.UserTokenCache.SetAfterAccessAsync(async args =>
        {
            if (args.HasStateChanged)
            {
                var bytes = args.TokenCache.SerializeMsalV3();
                var cacheBlob = Convert.ToBase64String(bytes);
                await _authRepo.SaveOAuthTokenAsync(new OAuthToken(
                    AccountId: _accountId,
                    AccessToken: cacheBlob,
                    RefreshToken: "msal-managed",
                    ExpiresAt: DateTimeOffset.UtcNow.AddDays(90)
                ));
            }
        });

        return _msalApp;
    }

    private string CacheToken(AuthenticationResult result)
    {
        _cachedAccessToken = result.AccessToken;
        _tokenExpiry = result.ExpiresOn;
        return _cachedAccessToken;
    }
}
