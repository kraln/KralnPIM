using PIM.Core.Config;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Setup.Views;

internal sealed class CleanupView : View
{
    private readonly SetupApp _app;
    private readonly TextView _output;

    public CleanupView(SetupApp app)
    {
        _app = app;
        CanFocus = true;

        var header = new Label { X = 2, Y = 0, Text = "Database Cleanup" };

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

        Initialized += (_, _) => { _ = RunCleanupAsync(); };
    }

    private async Task RunCleanupAsync()
    {
        try
        {
            _app.InitializeDb();
            if (_app.CalendarRepo is null)
            {
                AppendLine("No database available.");
                return;
            }

            var totalDeleted = 0;

            foreach (var account in _app.Config.Accounts)
            {
                var deleted = await CleanupAccountAsync(account);
                totalDeleted += deleted;
            }

            AppendLine("");
            AppendLine(totalDeleted > 0
                ? $"Done. Removed {totalDeleted} events from deselected calendars."
                : "Done. No stale events found.");
        }
        catch (Exception ex)
        {
            AppendLine($"Error: {ex.Message}");
        }
    }

    private async Task<int> CleanupAccountAsync(AccountConfig account)
    {
        // Only clean up accounts that have a calendar filter configured
        if (account.Calendars is null || account.Calendars.Count == 0)
        {
            if (account.Type is AccountType.Google or AccountType.Office365)
                AppendLine($"{account.DisplayName}: No calendar filter — syncing all, skipped.");
            return 0;
        }

        var keepIds = account.Calendars.Select(c => c.Id).ToHashSet();

        AppendLine($"{account.DisplayName}: keeping {keepIds.Count} calendars...");
        var deleted = await _app.CalendarRepo!.DeleteEventsNotInCalendarsAsync(
            account.Id, keepIds);

        if (deleted > 0)
            AppendLine($"  Removed {deleted} events from deselected calendars.");
        else
            AppendLine($"  Clean — no stale events.");

        return deleted;
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
