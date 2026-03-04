using PIM.Core;
using PIM.Core.Config;
using PIM.Setup.Auth;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Setup.Views;

internal sealed class AuthenticateView : View
{
    private readonly SetupApp _app;
    private readonly AccountConfig _account;
    private readonly TextView _output;

    public AuthenticateView(SetupApp app, AccountConfig account)
    {
        _app = app;
        _account = account;
        CanFocus = true;

        var header = new Label { X = 2, Y = 0, Text = $"Authenticate: {account.DisplayName} ({account.Type})" };

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
        close.Accepting += (_, e) => { _app.ShowView(new AccountListView(_app)); e.Handled = true; };

        Add(header, _output, close);

        Initialized += (_, _) => _ = RunAuthAsync();
    }

    private async Task RunAuthAsync()
    {
        try
        {
            _app.InitializeDb();

            if (_app.AuthRepo is null)
            {
                AppendLine("[FAIL] No database connection.");
                return;
            }

            switch (_account.Type)
            {
                case AccountType.Google:
                    AppendLine("Starting Google OAuth...");
                    var gCid = _account.ClientId ?? DefaultCredentials.Google.ClientId;
                    var gSec = _account.ClientSecret ?? DefaultCredentials.Google.ClientSecret;
                    var googleOk = await GoogleAuthFlow.AuthorizeAsync(
                        gCid, gSec, _account.Id,
                        _app.AuthRepo, AppendLine, CancellationToken.None);
                    AppendLine(googleOk ? "[OK] Google token acquired." : "[FAIL] Google auth failed.");
                    break;

                case AccountType.Office365:
                    AppendLine("Starting O365 device code flow...");
                    var oCid = _account.ClientId ?? DefaultCredentials.Office365.ClientId;
                    var oTid = _account.TenantId ?? DefaultCredentials.Office365.TenantId;
                    var graphOk = await GraphAuthFlow.AuthorizeAsync(
                        oCid, oTid, _account.Id,
                        _app.AuthRepo, AppendLine, CancellationToken.None);
                    AppendLine(graphOk ? "[OK] O365 token acquired." : "[FAIL] O365 auth failed.");
                    break;

                default:
                    AppendLine($"Account type '{_account.Type}' uses password auth, not OAuth.");
                    AppendLine("Use Edit to change the password.");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppendLine($"[FAIL] {ex.Message}");
        }
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
