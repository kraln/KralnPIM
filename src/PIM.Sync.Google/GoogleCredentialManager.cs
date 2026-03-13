using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;
using Microsoft.Extensions.Logging;
using PIM.Core.Data;
using PIM.Core.Providers;

namespace PIM.Sync.Google;

public sealed class GoogleCredentialManager
{
    private readonly string _accountId;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly IAuthRepository _authRepo;
    private readonly ILogger _logger;
    private UserCredential? _credential;

    /// <summary>
    /// Callback invoked when interactive OAuth is needed, providing the auth URL.
    /// </summary>
    public Action<string>? OnAuthUrlNeeded { get; set; }

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
            if (!_credential.Token.IsStale)
                return _credential;

            // Attempt to refresh if the token is expired
            try
            {
                var refreshed = await _credential.RefreshTokenAsync(ct);
                if (refreshed)
                    return _credential;
            }
            catch (TokenResponseException ex) when (IsTokenRevoked(ex))
            {
                _logger.LogWarning("Token revoked for {AccountId}, clearing credential", _accountId);
                _credential = null;
                throw new ReauthorizationRequiredException(_accountId, ex);
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

            if (_credential.Token.IsStale)
            {
                try
                {
                    await _credential.RefreshTokenAsync(ct);
                }
                catch (TokenResponseException ex) when (IsTokenRevoked(ex))
                {
                    _logger.LogWarning("Stored token revoked for {AccountId}, clearing credential and stored token", _accountId);
                    _credential = null;
                    await _authRepo.DeleteOAuthTokenAsync(_accountId, ct);
                    throw new ReauthorizationRequiredException(_accountId, ex);
                }
            }

            return _credential;
        }

        // No stored token — need interactive OAuth flow
        if (OnAuthUrlNeeded is null)
        {
            // Not in interactive re-auth context — cannot proceed without user interaction
            throw new ReauthorizationRequiredException(_accountId);
        }

        _credential = await GoogleOAuthHelper.AuthorizeAsync(
            _clientId, _clientSecret, _accountId, _authRepo, _logger, ct, OnAuthUrlNeeded);

        return _credential;
    }

    private static bool IsTokenRevoked(TokenResponseException ex) =>
        ex.Error?.Error is "invalid_grant";
}
