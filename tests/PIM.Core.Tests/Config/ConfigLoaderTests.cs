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

        Assert.Equal(3, config.Accounts.Count);

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
        Assert.NotNull(imap.Calendars);
        Assert.Single(imap.Calendars);
        Assert.Equal("personal-caldav", imap.Calendars[0].Id);
        Assert.Equal(CalendarType.CalDav, imap.Calendars[0].Type);
        Assert.Equal("https://radicale.example.com/user/calendar.ics", imap.Calendars[0].Url);

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
    public void Load_GoogleNoClient_ThrowsValidation()
    {
        var ex = Assert.Throws<ConfigValidationException>(() =>
            ConfigLoader.Load(GetTestConfigPath("invalid_google_no_client.yaml")));
        Assert.Contains(ex.Errors, e => e.Contains("client_id"));
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
}
