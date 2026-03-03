using Microsoft.Identity.Client;
using PIM.Core.Data;
using PIM.Core.Models;

namespace PIM.Setup.Auth;

internal static class GraphAuthFlow
{
    private static readonly string[] Scopes = ["Mail.ReadWrite", "Mail.Send", "Calendars.ReadWrite"];

    public static async Task<bool> AuthorizeAsync(
        string clientId,
        string tenantId,
        string accountId,
        IAuthRepository authRepo,
        Action<string> onStatus,
        CancellationToken ct)
    {
        var app = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .Build();

        // Wire token cache serialization to IAuthRepository
        app.UserTokenCache.SetBeforeAccessAsync(async args =>
        {
            var stored = await authRepo.GetOAuthTokenAsync(accountId, ct);
            if (stored is null || string.IsNullOrEmpty(stored.AccessToken))
                return;

            try
            {
                var bytes = Convert.FromBase64String(stored.AccessToken);
                args.TokenCache.DeserializeMsalV3(bytes);
            }
            catch (FormatException)
            {
                // Not a valid MSAL cache blob (first run or legacy token format)
            }
        });

        app.UserTokenCache.SetAfterAccessAsync(async args =>
        {
            if (args.HasStateChanged)
            {
                var bytes = args.TokenCache.SerializeMsalV3();
                var cacheBlob = Convert.ToBase64String(bytes);
                await authRepo.SaveOAuthTokenAsync(new OAuthToken(
                    AccountId: accountId,
                    AccessToken: cacheBlob,
                    RefreshToken: "msal-managed",
                    ExpiresAt: DateTimeOffset.UtcNow.AddDays(90)
                ), ct);
            }
        });

        // Try silent acquisition from MSAL in-memory + serialized cache
        var accounts = await app.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        if (account is not null)
        {
            try
            {
                await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(ct);
                onStatus("Existing O365 authorization found.");
                return true;
            }
            catch (MsalUiRequiredException)
            {
                // Token expired and refresh failed, fall through
            }
        }

        // No cached MSAL account -- try restoring from IAuthRepository
        var stored = await authRepo.GetOAuthTokenAsync(accountId, ct);
        if (stored is not null && !string.IsNullOrEmpty(stored.AccessToken))
        {
            // Cache was deserialized in SetBeforeAccessAsync, retry silent
            accounts = await app.GetAccountsAsync();
            account = accounts.FirstOrDefault();
            if (account is not null)
            {
                try
                {
                    await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(ct);
                    onStatus("Restored O365 authorization from saved token.");
                    return true;
                }
                catch (MsalUiRequiredException)
                {
                    // Fall through to device code
                }
            }
        }

        // Interactive: device code flow
        onStatus("Starting O365 device code authorization...");
        var result = await app.AcquireTokenWithDeviceCode(Scopes, callback =>
        {
            onStatus($"Visit {callback.VerificationUrl} and enter code {callback.UserCode}");
            return Task.CompletedTask;
        }).ExecuteAsync(ct);

        // Persist token via IAuthRepository (MSAL cache handles refresh tokens)
        await authRepo.SaveOAuthTokenAsync(new OAuthToken(
            AccountId: accountId,
            AccessToken: result.AccessToken,
            RefreshToken: "msal-managed",
            ExpiresAt: result.ExpiresOn
        ), ct);

        onStatus("O365 authorization successful.");
        return true;
    }
}
