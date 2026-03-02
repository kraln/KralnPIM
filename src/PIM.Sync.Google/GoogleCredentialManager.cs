using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;
using Microsoft.Extensions.Logging;
using PIM.Core.Data;

namespace PIM.Sync.Google;

public sealed class GoogleCredentialManager
{
    private readonly string _accountId;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly IAuthRepository _authRepo;
    private readonly ILogger _logger;
    private UserCredential? _credential;

    public GoogleCredentialManager(
        string accountId,
        string clientId,
        string clientSecret,
        IAuthRepository authRepo,
        ILogger logger)
    {
        _accountId = accountId;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _authRepo = authRepo;
        _logger = logger;
    }

    public async Task<UserCredential> EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_credential is not null)
        {
            // Attempt to refresh if the token is expired
            if (_credential.Token.IsStale)
            {
                var refreshed = await _credential.RefreshTokenAsync(ct);
                if (refreshed)
                    return _credential;
            }
            else
            {
                return _credential;
            }
        }

        // Try to load from repository
        var existing = await _authRepo.GetOAuthTokenAsync(_accountId, ct);
        if (existing is not null && !string.IsNullOrEmpty(existing.RefreshToken))
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret,
                },
                Scopes =
                [
                    GmailService.Scope.GmailModify,
                    CalendarService.Scope.CalendarEvents,
                    CalendarService.Scope.CalendarReadonly,
                ],
                DataStore = new AuthRepositoryDataStore(_authRepo, _accountId),
            });

            var tokenResponse = new TokenResponse
            {
                AccessToken = existing.AccessToken,
                RefreshToken = existing.RefreshToken,
                ExpiresInSeconds = (long)(existing.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds,
                IssuedUtc = DateTime.UtcNow,
            };

            _credential = new UserCredential(flow, _accountId, tokenResponse);

            // Refresh if token is expired
            if (_credential.Token.IsStale)
                await _credential.RefreshTokenAsync(ct);

            return _credential;
        }

        // No stored token — run interactive OAuth flow
        _credential = await GoogleOAuthHelper.AuthorizeAsync(
            _clientId, _clientSecret, _accountId, _authRepo, _logger, ct);

        return _credential;
    }
}
