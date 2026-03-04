using Microsoft.Extensions.Logging;
using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Setup.Config;
using PIM.Setup.Views;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Setup;

internal sealed class SetupApp : Window
{
    private readonly string _configPath;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Label _statusLabel;
    private View? _currentView;
    private object? _statusClearTimeout;
    private bool _hasUnsavedChanges;
    private bool _exitConfirmPending;

    public PimConfig Config { get; set; }
    public DbConnectionFactory? DbFactory { get; private set; }
    public IAuthRepository? AuthRepo { get; private set; }

    public SetupApp(string configPath, ILoggerFactory loggerFactory)
    {
        _configPath = configPath;
        _loggerFactory = loggerFactory;
        Title = "KralnPIM Setup (Esc to go back)";

        Config = ConfigSerializer.LoadOrDefault(configPath);

        _statusLabel = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            Text = "Ready"
        };

        Add(_statusLabel);

        // Remove the default Window Esc → QuitToplevel binding so child views can use Esc for navigation
        KeyBindings.Remove(Key.Esc);

        KeyDown += (_, e) =>
        {
            if (e == Key.Esc)
            {
                if (_currentView is MainMenuView)
                    ConfirmExit();
                else
                    ShowMainMenu();
                e.Handled = true;
            }
        };

        Initialized += (_, _) =>
        {
            InitializeDb();
            ShowView(new MainMenuView(this));
        };
    }

    public string ConfigPath => _configPath;

    public void MarkChanged() => _hasUnsavedChanges = true;

    public void ShowView(View view)
    {
        if (_currentView is not null)
        {
            Remove(_currentView);
            _currentView.Dispose();
        }

        _currentView = view;
        _currentView.X = 0;
        _currentView.Y = 0;
        _currentView.Width = Dim.Fill();
        _currentView.Height = Dim.Fill(1);
        Add(_currentView);
        // Defer focus to next event loop iteration — SetFocus fails if called
        // before Terminal.Gui completes layout for the newly added view
        App?.Invoke(() => _currentView?.SetFocus());
    }

    public void ShowMainMenu()
    {
        ShowView(new MainMenuView(this));
    }

    public void ShowStatus(string message)
    {
        _statusLabel.Text = message;
        ClearStatusAfterDelay();
    }

    public void ShowError(string message)
    {
        _statusLabel.Text = $"Error: {message}";
        ClearStatusAfterDelay();
    }

    public bool SaveConfig()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            ConfigSerializer.Save(Config, _configPath);
            InitializeDb();
            _hasUnsavedChanges = false;
            ShowStatus("Configuration saved.");
            return true;
        }
        catch (ConfigValidationException ex)
        {
            ShowError(string.Join("; ", ex.Errors));
            return false;
        }
        catch (Exception ex)
        {
            ShowError($"Failed to save: {ex.Message}");
            return false;
        }
    }

    public bool ConfirmExit()
    {
        if (!_hasUnsavedChanges)
        {
            App?.RequestStop();
            return true;
        }

        // Two-press confirmation
        if (_exitConfirmPending)
        {
            App?.RequestStop();
            return true;
        }

        _exitConfirmPending = true;
        ShowStatus("Unsaved changes! Press Exit again to discard, or Save & Exit to keep.");
        return false;
    }

    public void InitializeDb()
    {
        try
        {
            var dbPath = Config.Storage.DbPath.Replace("~",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            DbFactory = new DbConnectionFactory(dbPath);
            AuthRepo = new SqliteAuthRepository(DbFactory);

            var sqlDir = FindSqlDirectory();
            if (sqlDir is not null)
            {
                var runner = new MigrationRunner(DbFactory,
                    _loggerFactory.CreateLogger<MigrationRunner>());
                runner.RunAsync(sqlDir).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            ShowError($"DB init failed: {ex.Message}");
        }
    }

    public async Task<string?> GetAuthStatusAsync(AccountConfig account)
    {
        if (AuthRepo is null)
            return "No DB";

        try
        {
            return account.Type switch
            {
                AccountType.Imap => await AuthRepo.GetImapPasswordAsync(account.Id) is not null
                    ? "Has password" : "No password",
                AccountType.CalDav => await AuthRepo.GetCalDavPasswordAsync(account.Id) is not null
                    ? "Has password" : "No password",
                AccountType.Google or AccountType.Office365 =>
                    await AuthRepo.GetOAuthTokenAsync(account.Id) is not null
                        ? "Has token" : "No token",
                _ => "Unknown"
            };
        }
        catch
        {
            return "Error";
        }
    }

    private void ClearStatusAfterDelay()
    {
        if (_statusClearTimeout is not null)
            App?.RemoveTimeout(_statusClearTimeout);

        _statusClearTimeout = App?.AddTimeout(TimeSpan.FromSeconds(5), () =>
        {
            _statusLabel.Text = "Ready";
            _statusClearTimeout = null;
            return false;
        });
    }

    private static string? FindSqlDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var sqlDir = Path.Combine(dir, "sql");
            if (Directory.Exists(sqlDir))
                return sqlDir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
