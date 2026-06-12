using System.Net.Http.Headers;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Identity.Client;
using PIM.Core;
using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Setup.Auth;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Setup.Views;

/// <summary>
/// Tests connection for a single account (when account is provided)
/// or all accounts sequentially (when account is null).
/// </summary>
internal sealed class ConnectionTestView : View
{
    private readonly SetupApp _app;
    private readonly AccountConfig? _account;
    private readonly TextView _output;
    private CancellationTokenSource? _cts;

    public ConnectionTestView(SetupApp app, AccountConfig? account)
    {
        _app = app;
        _account = account;
        CanFocus = true;

        var title = account is not null
            ? $"Testing: {account.DisplayName} ({account.Type})"
            : "Testing All Accounts";

        var header = new Label { X = 2, Y = 0, Text = title };

        _output = new TextView
        {
            X = 2, Y = 2,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            ReadOnly = true,
            WordWrap = true,
            Text = "",
        };

        var close = new Button { X = Pos.AnchorEnd(12), Y = Pos.AnchorEnd(2), Text = "Close" };
        close.Accepting += (_, e) => { _app.ShowMainMenu(); e.Handled = true; };

        Add(header, _output, close);

        Initialized += (_, _) =>
        {
            _ = RunTestsAsync();
        };
    }

    private async Task RunTestsAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var accounts = _account is not null
            ? [_account]
            : _app.Config.Accounts;

        if (accounts.Count == 0)
        {
            AppendLine("No accounts configured.");
            return;
        }

        foreach (var account in accounts)
        {
            if (ct.IsCancellationRequested) break;

            AppendLine($"--- {account.DisplayName} ({account.Type}) ---");

            try
            {
                await TestAccountAsync(account, ct);
            }
            catch (OperationCanceledException)
            {
                AppendLine("  Cancelled.");
                break;
            }
            catch (Exception ex)
            {
                AppendLine($"  Error: {ex.Message}");
                for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
                    AppendLine($"    -> {inner.Message}");
            }

            AppendLine("");
        }

        AppendLine("Done.");
    }

    private async Task TestAccountAsync(AccountConfig account, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var linked = timeout.Token;

        switch (account.Type)
        {
            case AccountType.Imap:
                await TestImapAsync(account, linked);
                await TestSmtpAsync(account, linked);
                break;
            case AccountType.Google:
                await TestGoogleTokenAsync(account, linked);
                break;
            case AccountType.Office365:
                await TestO365TokenAsync(account, linked);
                break;
            case AccountType.CalDav:
                await TestCalDavAsync(account, linked);
                break;
        }
    }

    private async Task TestImapAsync(AccountConfig account, CancellationToken ct)
    {
        if (_app.AuthRepo is null) { AppendLine("  IMAP: No database available."); return; }
        if (string.IsNullOrEmpty(account.ImapHost)) { AppendLine("  IMAP: No host configured."); return; }

        AppendLine("  IMAP: Connecting...");

        var password = await _app.AuthRepo.GetImapPasswordAsync(account.Id, ct);
        if (password is null) { AppendLine("  IMAP: No password stored."); return; }

        using var client = new ImapClient();
        if (account.IgnoreSslErrors == true)
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        var options = account.ImapTls == true
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(account.ImapHost, account.ImapPort ?? 993, options, ct);
        await client.AuthenticateAsync(account.Username ?? "", password, ct);

        var inbox = client.Inbox
            ?? throw new InvalidOperationException("IMAP server has no INBOX folder.");
        await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly, ct);
        var condstore = client.Capabilities.HasFlag(ImapCapabilities.CondStore);
        var folders = await client.GetFoldersAsync(client.PersonalNamespaces[0], cancellationToken: ct);

        AppendLine($"  IMAP: Connected to {account.ImapHost}:{account.ImapPort ?? 993}");
        AppendLine($"  IMAP: CONDSTORE={condstore}, {folders.Count} folders, {inbox.Count} messages in INBOX");

        await client.DisconnectAsync(true, ct);
        AppendLine("  IMAP: OK");
    }

    private async Task TestSmtpAsync(AccountConfig account, CancellationToken ct)
    {
        if (_app.AuthRepo is null) { AppendLine("  SMTP: No database available."); return; }
        if (string.IsNullOrEmpty(account.SmtpHost)) { AppendLine("  SMTP: No host configured."); return; }

        AppendLine("  SMTP: Connecting...");

        var password = await _app.AuthRepo.GetImapPasswordAsync(account.Id, ct);
        if (password is null) { AppendLine("  SMTP: No password stored."); return; }

        using var client = new SmtpClient();
        if (account.IgnoreSslErrors == true)
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        await client.ConnectAsync(account.SmtpHost, account.SmtpPort ?? 587, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(account.Username ?? "", password, ct);

        AppendLine($"  SMTP: Connected to {account.SmtpHost}:{account.SmtpPort ?? 587}");

        await client.DisconnectAsync(true, ct);
        AppendLine("  SMTP: OK");
    }

    private async Task TestGoogleTokenAsync(AccountConfig account, CancellationToken ct)
    {
        if (_app.AuthRepo is null) { AppendLine("  Google: No database available."); return; }

        AppendLine("  Google: Checking token...");

        var oauthToken = await _app.AuthRepo.GetOAuthTokenAsync(account.Id, ct);
        if (oauthToken is null)
        {
            AppendLine("  Google: No token — run authentication first.");
            return;
        }

        var gCid = account.ClientId ?? DefaultCredentials.Google.ClientId;
        var gSec = account.ClientSecret ?? DefaultCredentials.Google.ClientSecret;

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = gCid,
                ClientSecret = gSec,
            },
            Scopes = ["https://www.googleapis.com/auth/gmail.modify"],
            DataStore = new AuthRepositoryDataStore(_app.AuthRepo, account.Id),
        });

        var token = await flow.LoadTokenAsync(account.Id, ct);
        if (token is null || string.IsNullOrEmpty(token.RefreshToken))
        {
            AppendLine("  Google: Token missing or no refresh token.");
            return;
        }

        if (token.IsStale)
        {
            AppendLine("  Google: Token expired, attempting refresh...");
            try
            {
                token = await flow.RefreshTokenAsync(account.Id, token.RefreshToken, ct);
                AppendLine($"  Google: Token refreshed, expires at {token.IssuedUtc.AddSeconds(token.ExpiresInSeconds ?? 3600):u}");
            }
            catch (TokenResponseException ex)
            {
                AppendLine($"  Google: Refresh failed — {ex.Error.Error}. Re-authentication needed.");
                return;
            }
        }
        else
        {
            var expiresAt = token.IssuedUtc.AddSeconds(token.ExpiresInSeconds ?? 3600);
            AppendLine($"  Google: Token valid, expires at {expiresAt:u}");
        }

        AppendLine("  Google: OK");
    }

    private async Task TestO365TokenAsync(AccountConfig account, CancellationToken ct)
    {
        if (_app.AuthRepo is null) { AppendLine("  O365: No database available."); return; }

        AppendLine("  O365: Checking token...");

        var stored = await _app.AuthRepo.GetOAuthTokenAsync(account.Id, ct);
        if (stored is null || string.IsNullOrEmpty(stored.AccessToken))
        {
            AppendLine("  O365: No token — run authentication first.");
            return;
        }

        var oCid = account.ClientId ?? DefaultCredentials.Office365.ClientId;
        var oTid = account.TenantId ?? DefaultCredentials.Office365.TenantId;

        var app = PublicClientApplicationBuilder
            .Create(oCid)
            .WithAuthority($"https://login.microsoftonline.com/{oTid}")
            .Build();

        // Deserialize MSAL cache
        app.UserTokenCache.SetBeforeAccessAsync(args =>
        {
            try
            {
                var bytes = Convert.FromBase64String(stored.AccessToken);
                args.TokenCache.DeserializeMsalV3(bytes);
            }
            catch (FormatException) { }

            return Task.CompletedTask;
        });

        var accounts = await app.GetAccountsAsync();
        var msalAccount = accounts.FirstOrDefault();

        if (msalAccount is null)
        {
            AppendLine("  O365: No cached account — re-authentication needed.");
            return;
        }

        try
        {
            string[] scopes = ["Mail.ReadWrite", "Calendars.ReadWrite"];
            var result = await app.AcquireTokenSilent(scopes, msalAccount).ExecuteAsync(ct);
            AppendLine($"  O365: Token valid, expires at {result.ExpiresOn:u}");
            AppendLine("  O365: OK");
        }
        catch (MsalUiRequiredException)
        {
            AppendLine("  O365: Token expired — re-authentication needed.");
        }
    }

    private async Task TestCalDavAsync(AccountConfig account, CancellationToken ct)
    {
        if (_app.AuthRepo is null) { AppendLine("  CalDAV: No database available."); return; }

        AppendLine("  CalDAV: Checking credentials...");

        var password = await _app.AuthRepo.GetCalDavPasswordAsync(account.Id, ct);
        if (password is null)
        {
            AppendLine("  CalDAV: No password stored.");
            return;
        }

        if (account.Calendars is null || account.Calendars.Count == 0)
        {
            AppendLine("  CalDAV: No calendars configured.");
            return;
        }

        var ignoreSsl = account.IgnoreSslErrors == true;
        AppendLine($"  CalDAV: IgnoreSslErrors={ignoreSsl}");

        SocketsHttpHandler handler = new();
        if (ignoreSsl)
            handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{account.Username}:{password}"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        foreach (var cal in account.Calendars)
        {
            if (string.IsNullOrEmpty(cal.Url))
            {
                AppendLine($"  CalDAV [{cal.Id}]: No URL configured.");
                continue;
            }

            AppendLine($"  CalDAV [{cal.Id}]: Testing {cal.Url}...");

            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), cal.Url);
            request.Headers.Add("Depth", "0");
            request.Content = new StringContent(
                "<?xml version=\"1.0\"?><propfind xmlns=\"DAV:\"><prop>" +
                "<getctag xmlns=\"http://calendarserver.org/ns/\"/>" +
                "</prop></propfind>",
                Encoding.UTF8, "application/xml");

            var response = await client.SendAsync(request, ct);
            var statusCode = (int)response.StatusCode;

            if (statusCode == 207)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var ctagIdx = body.IndexOf("<cs:getctag>", StringComparison.Ordinal);
                var ctagEnd = body.IndexOf("</cs:getctag>", StringComparison.Ordinal);

                string ctag;
                if (ctagIdx >= 0 && ctagEnd > ctagIdx)
                    ctag = body[(ctagIdx + 12)..ctagEnd];
                else
                    ctag = "(not reported)";

                AppendLine($"  CalDAV [{cal.Id}]: Connected, ctag={ctag}");
            }
            else if (statusCode == 401)
            {
                AppendLine($"  CalDAV [{cal.Id}]: Authentication failed (401).");
            }
            else
            {
                AppendLine($"  CalDAV [{cal.Id}]: HTTP {statusCode}: {response.ReasonPhrase}");
            }
        }

        AppendLine("  CalDAV: Done");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cts?.Cancel();
        base.Dispose(disposing);
    }

    private void AppendLine(string line)
    {
        App?.Invoke(() =>
        {
            _output.Text = _output.Text + line + "\n";
            _output.MoveEnd();
        });
    }
}
