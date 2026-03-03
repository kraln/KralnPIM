using PIM.Core.Config;
using PIM.Setup.Config;

namespace PIM.Setup.Tests.Config;

public class ConfigSerializerTests
{
    [Fact]
    public void CreateDefault_ReturnsValidConfig()
    {
        var config = ConfigSerializer.CreateDefault();

        Assert.Empty(config.Accounts);
        Assert.Equal("UTC", config.Ui.TimezonePrimary);
        Assert.Null(config.Ui.TimezoneSecondary);
        Assert.Equal("open-meteo", config.System.WeatherProvider);
        Assert.Null(config.System.WeatherLocation);
        Assert.Equal("~/.pim/pim.db", config.Storage.DbPath);
        Assert.Equal("~/Downloads/pim-attachments", config.Storage.AttachmentDownloadDir);
        Assert.Equal(6, config.Storage.BufferMonthsBack);
        Assert.Equal(6, config.Storage.BufferMonthsForward);
        Assert.Equal("127.0.0.1", config.Server.ListenAddress);
        Assert.Equal(9400, config.Server.RestPort);
        Assert.Equal(9401, config.Server.WsPort);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesAllFields()
    {
        var config = new PimConfig(
            Accounts:
            [
                new AccountConfig(
                    Id: "test-imap",
                    Type: AccountType.Imap,
                    DisplayName: "Test IMAP",
                    ImapHost: "imap.example.com",
                    ImapPort: 993,
                    ImapTls: true,
                    SmtpHost: "smtp.example.com",
                    SmtpPort: 587,
                    Username: "user@example.com",
                    ClientId: null,
                    ClientSecret: null,
                    TenantId: null,
                    Calendars: null),
                new AccountConfig(
                    Id: "test-google",
                    Type: AccountType.Google,
                    DisplayName: "Test Google",
                    ImapHost: null,
                    ImapPort: null,
                    ImapTls: null,
                    SmtpHost: null,
                    SmtpPort: null,
                    Username: null,
                    ClientId: "client-id",
                    ClientSecret: "client-secret",
                    TenantId: null,
                    Calendars: []),
                new AccountConfig(
                    Id: "test-caldav",
                    Type: AccountType.CalDav,
                    DisplayName: "Test CalDAV",
                    ImapHost: null,
                    ImapPort: null,
                    ImapTls: null,
                    SmtpHost: null,
                    SmtpPort: null,
                    Username: "cal-user",
                    ClientId: null,
                    ClientSecret: null,
                    TenantId: null,
                    Calendars:
                    [
                        new CalendarSourceConfig("home", CalendarType.CalDav, "https://cal.example.com/home.ics"),
                    ]),
            ],
            Ui: new UiConfig("America/New_York", "Europe/London"),
            System: new SystemConfig("40.7,-74.0", "open-meteo"),
            Storage: new StorageConfig("~/.pim/test.db", "~/Downloads/att", 3, 12),
            Server: new ServerConfig("0.0.0.0", 8080, 8081)
        );

        var path = Path.Combine(Path.GetTempPath(), $"pim_test_{Guid.NewGuid()}.yaml");
        try
        {
            ConfigSerializer.Save(config, path);
            var loaded = ConfigSerializer.Load(path);

            // Accounts
            Assert.Equal(3, loaded.Accounts.Count);

            var imap = loaded.Accounts[0];
            Assert.Equal("test-imap", imap.Id);
            Assert.Equal(AccountType.Imap, imap.Type);
            Assert.Equal("Test IMAP", imap.DisplayName);
            Assert.Equal("imap.example.com", imap.ImapHost);
            Assert.Equal(993, imap.ImapPort);
            Assert.True(imap.ImapTls);
            Assert.Equal("smtp.example.com", imap.SmtpHost);
            Assert.Equal(587, imap.SmtpPort);
            Assert.Equal("user@example.com", imap.Username);

            var google = loaded.Accounts[1];
            Assert.Equal("test-google", google.Id);
            Assert.Equal(AccountType.Google, google.Type);
            Assert.Equal("client-id", google.ClientId);
            Assert.Equal("client-secret", google.ClientSecret);

            var caldav = loaded.Accounts[2];
            Assert.Equal("test-caldav", caldav.Id);
            Assert.Equal(AccountType.CalDav, caldav.Type);
            Assert.Equal("cal-user", caldav.Username);
            Assert.NotNull(caldav.Calendars);
            Assert.Single(caldav.Calendars);
            Assert.Equal("home", caldav.Calendars[0].Id);
            Assert.Equal(CalendarType.CalDav, caldav.Calendars[0].Type);
            Assert.Equal("https://cal.example.com/home.ics", caldav.Calendars[0].Url);

            // Settings
            Assert.Equal("America/New_York", loaded.Ui.TimezonePrimary);
            Assert.Equal("Europe/London", loaded.Ui.TimezoneSecondary);
            Assert.Equal("40.7,-74.0", loaded.System.WeatherLocation);
            Assert.Equal("open-meteo", loaded.System.WeatherProvider);
            Assert.Equal("~/.pim/test.db", loaded.Storage.DbPath);
            Assert.Equal("~/Downloads/att", loaded.Storage.AttachmentDownloadDir);
            Assert.Equal(3, loaded.Storage.BufferMonthsBack);
            Assert.Equal(12, loaded.Storage.BufferMonthsForward);
            Assert.Equal("0.0.0.0", loaded.Server.ListenAddress);
            Assert.Equal(8080, loaded.Server.RestPort);
            Assert.Equal(8081, loaded.Server.WsPort);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveAndLoad_O365Account_PreservesTenantId()
    {
        var config = ConfigSerializer.CreateDefault() with
        {
            Accounts =
            [
                new AccountConfig(
                    Id: "test-o365",
                    Type: AccountType.Office365,
                    DisplayName: "Test O365",
                    ImapHost: null, ImapPort: null, ImapTls: null,
                    SmtpHost: null, SmtpPort: null,
                    Username: null,
                    ClientId: "o365-client",
                    ClientSecret: null,
                    TenantId: "tenant-abc",
                    Calendars: []),
            ],
        };

        var path = Path.Combine(Path.GetTempPath(), $"pim_test_{Guid.NewGuid()}.yaml");
        try
        {
            ConfigSerializer.Save(config, path);
            var loaded = ConfigSerializer.Load(path);

            Assert.Single(loaded.Accounts);
            var o365 = loaded.Accounts[0];
            Assert.Equal(AccountType.Office365, o365.Type);
            Assert.Equal("tenant-abc", o365.TenantId);
            Assert.Equal("o365-client", o365.ClientId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_NullOptionalFields_OmitsFromYaml()
    {
        var config = ConfigSerializer.CreateDefault();

        var path = Path.Combine(Path.GetTempPath(), $"pim_test_{Guid.NewGuid()}.yaml");
        try
        {
            ConfigSerializer.Save(config, path);
            var yaml = File.ReadAllText(path);

            // Null fields should not appear in output
            Assert.DoesNotContain("timezone_secondary", yaml);
            Assert.DoesNotContain("weather_location", yaml);
            Assert.DoesNotContain("imap_host", yaml);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrDefault_NonExistentFile_ReturnsDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.yaml");
        var config = ConfigSerializer.LoadOrDefault(path);

        Assert.Empty(config.Accounts);
        Assert.Equal("UTC", config.Ui.TimezonePrimary);
    }

    [Fact]
    public void LoadOrDefault_ExistingFile_LoadsIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pim_test_{Guid.NewGuid()}.yaml");
        try
        {
            var original = ConfigSerializer.CreateDefault() with
            {
                Ui = new UiConfig("America/Chicago", null),
                Accounts =
                [
                    new AccountConfig(
                        Id: "test", Type: AccountType.Imap, DisplayName: "Test",
                        ImapHost: "imap.test.com", ImapPort: 993, ImapTls: true,
                        SmtpHost: "smtp.test.com", SmtpPort: 587,
                        Username: "u@test.com",
                        ClientId: null, ClientSecret: null, TenantId: null,
                        Calendars: null),
                ],
            };
            ConfigSerializer.Save(original, path);

            var loaded = ConfigSerializer.LoadOrDefault(path);
            Assert.Equal("America/Chicago", loaded.Ui.TimezonePrimary);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
