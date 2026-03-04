using PIM.Core.Config;

namespace PIM.Core.Tests.Config;

public class ConfigLoaderTests
{
    private static string GetTestConfigPath(string filename)
    {
        return Path.Combine(AppContext.BaseDirectory, "Config", "TestConfigs", filename);
    }

    [Fact]
    public void Load_ValidFullConfig_ParsesAllFields()
    {
        var config = ConfigLoader.Load(GetTestConfigPath("valid_full.yaml"));

        Assert.Equal(4, config.Accounts.Count);

        var imap = config.Accounts[0];
        Assert.Equal("personal-imap", imap.Id);
        Assert.Equal(AccountType.Imap, imap.Type);
        Assert.Equal("Personal", imap.DisplayName);
        Assert.Equal("mail.example.com", imap.ImapHost);
        Assert.Equal(993, imap.ImapPort);
        Assert.True(imap.ImapTls);
        Assert.Equal("mail.example.com", imap.SmtpHost);
        Assert.Equal(587, imap.SmtpPort);
        Assert.Equal("user@example.com", imap.Username);

        var google = config.Accounts[1];
        Assert.Equal("work-google", google.Id);
        Assert.Equal(AccountType.Google, google.Type);
        Assert.Equal("xxx.apps.googleusercontent.com", google.ClientId);
        Assert.Equal("GOCSPX-xxx", google.ClientSecret);

        var o365 = config.Accounts[2];
        Assert.Equal("work-o365", o365.Id);
        Assert.Equal(AccountType.Office365, o365.Type);
        Assert.Equal("xxxx-xxxx", o365.TenantId);
        Assert.Equal("xxxx-xxxx", o365.ClientId);

        var caldav = config.Accounts[3];
        Assert.Equal("my-radicale", caldav.Id);
        Assert.Equal(AccountType.CalDav, caldav.Type);
        Assert.Equal("Radicale", caldav.DisplayName);
        Assert.Equal("user@example.com", caldav.Username);
        Assert.NotNull(caldav.Calendars);
        Assert.Single(caldav.Calendars);
        Assert.Equal("personal-caldav", caldav.Calendars[0].Id);
        Assert.Equal(CalendarType.CalDav, caldav.Calendars[0].Type);
        Assert.Equal("https://radicale.example.com/user/calendar.ics", caldav.Calendars[0].Url);

        Assert.Equal("America/New_York", config.Ui.TimezonePrimary);
        Assert.Equal("Europe/London", config.Ui.TimezoneSecondary);
        Assert.Equal("40.7128,-74.0060", config.System.WeatherLocation);
        Assert.Equal("open-meteo", config.System.WeatherProvider);
        Assert.Equal("~/.pim/pim.db", config.Storage.DbPath);
        Assert.Equal(6, config.Storage.BufferMonthsBack);
        Assert.Equal(6, config.Storage.BufferMonthsForward);
        Assert.Equal("127.0.0.1", config.Server.ListenAddress);
        Assert.Equal(9400, config.Server.RestPort);
        Assert.Equal(9401, config.Server.WsPort);
    }

    [Fact]
    public void Load_ValidMinimalConfig_ParsesCorrectly()
    {
        var config = ConfigLoader.Load(GetTestConfigPath("valid_minimal.yaml"));

        Assert.Single(config.Accounts);
        Assert.Equal("personal", config.Accounts[0].Id);
        Assert.Equal(AccountType.Imap, config.Accounts[0].Type);
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        Assert.ThrowsAny<IOException>(() =>
            ConfigLoader.Load("/nonexistent/path/config.yaml"));
    }

    [Fact]
    public void Load_MissingAccountId_ThrowsValidation()
    {
        var ex = Assert.Throws<ConfigValidationException>(() =>
            ConfigLoader.Load(GetTestConfigPath("invalid_missing_id.yaml")));
        Assert.Contains(ex.Errors, e => e.Contains("'id' is required"));
    }

    [Fact]
    public void Load_ImapNoHost_ThrowsValidation()
    {
        var ex = Assert.Throws<ConfigValidationException>(() =>
            ConfigLoader.Load(GetTestConfigPath("invalid_imap_no_host.yaml")));
        Assert.Contains(ex.Errors, e => e.Contains("imap_host"));
    }

    [Fact]
    public void Load_GoogleNoClient_Succeeds()
    {
        // Google accounts without client_id are valid — embedded defaults in DefaultCredentials
        var config = ConfigLoader.Load(GetTestConfigPath("invalid_google_no_client.yaml"));
        Assert.Single(config.Accounts);
        Assert.Null(config.Accounts[0].ClientId);
    }

    [Fact]
    public void Load_BadPort_ThrowsValidation()
    {
        var ex = Assert.Throws<ConfigValidationException>(() =>
            ConfigLoader.Load(GetTestConfigPath("invalid_bad_port.yaml")));
        Assert.Contains(ex.Errors, e => e.Contains("rest_port"));
    }

    [Fact]
    public void Load_DuplicateAccountIds_ThrowsValidation()
    {
        var yaml = """
            accounts:
              - id: "same-id"
                type: imap
                display_name: "First"
                imap_host: "mail.example.com"
                imap_port: 993
                smtp_host: "mail.example.com"
                smtp_port: 587
                username: "user@example.com"
              - id: "same-id"
                type: imap
                display_name: "Second"
                imap_host: "mail2.example.com"
                imap_port: 993
                smtp_host: "mail2.example.com"
                smtp_port: 587
                username: "user2@example.com"
            ui:
              timezone_primary: "UTC"
            system:
              weather_provider: "open-meteo"
            storage:
              db_path: "~/.pim/pim.db"
              attachment_download_dir: "~/Downloads"
              buffer_months_back: 3
              buffer_months_forward: 3
            server:
              listen_address: "127.0.0.1"
              rest_port: 9400
              ws_port: 9401
            """;

        var ex = Assert.Throws<ConfigValidationException>(() =>
            ConfigLoader.LoadFromString(yaml));
        Assert.Contains(ex.Errors, e => e.Contains("Duplicate"));
    }

    [Fact]
    public void Load_EmptyConfig_ThrowsValidation()
    {
        var ex = Assert.Throws<ConfigValidationException>(() =>
            ConfigLoader.LoadFromString("{}"));
        Assert.Contains(ex.Errors, e => e.Contains("At least one account"));
    }

    [Fact]
    public void Load_CalDavNoCalendar_ThrowsValidation()
    {
        var ex = Assert.Throws<ConfigValidationException>(() =>
            ConfigLoader.Load(GetTestConfigPath("invalid_caldav_no_calendar.yaml")));
        Assert.Contains(ex.Errors, e => e.Contains("at least one calendar"));
    }

    [Fact]
    public void Load_CalDavNoUsername_ThrowsValidation()
    {
        var yaml = """
            accounts:
              - id: "my-caldav"
                type: caldav
                display_name: "CalDAV"
                calendars:
                  - id: "cal1"
                    type: caldav
                    url: "https://example.com/cal.ics"
            ui:
              timezone_primary: "UTC"
            system:
              weather_provider: "open-meteo"
            storage:
              db_path: "~/.pim/pim.db"
              attachment_download_dir: "~/Downloads"
              buffer_months_back: 3
              buffer_months_forward: 3
            server:
              listen_address: "127.0.0.1"
              rest_port: 9400
              ws_port: 9401
            """;

        var ex = Assert.Throws<ConfigValidationException>(() =>
            ConfigLoader.LoadFromString(yaml));
        Assert.Contains(ex.Errors, e => e.Contains("username") && e.Contains("CalDAV"));
    }
}
