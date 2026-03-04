using PIM.Core.Config;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PIM.Setup.Config;

public static class ConfigSerializer
{
    public static PimConfig Load(string path) => ConfigLoader.Load(path);

    /// <summary>
    /// Load config, falling back to loading without validation if validation fails.
    /// PIM.Setup is a config editor — it must load broken configs so users can fix them.
    /// </summary>
    public static PimConfig LoadOrDefault(string path)
    {
        if (!File.Exists(path))
            return CreateDefault();

        try
        {
            return ConfigLoader.Load(path);
        }
        catch (ConfigValidationException)
        {
            // Load without validation so the user can fix issues in the editor
            return ConfigLoader.LoadWithoutValidation(path);
        }
    }

    public static PimConfig CreateDefault() => new(
        Accounts: [],
        Ui: new UiConfig("UTC", null),
        System: new SystemConfig(null, "open-meteo"),
        Storage: new StorageConfig("~/.pim/pim.db", "~/Downloads/pim-attachments", 6, 6),
        Server: new ServerConfig("127.0.0.1", 9400, 9401)
    );

    public static void Save(PimConfig config, string path)
    {
        var dto = MapToDto(config);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        var yaml = serializer.Serialize(dto);
        File.WriteAllText(path, yaml);
    }

    private static PimConfigDto MapToDto(PimConfig config) => new()
    {
        Accounts = config.Accounts.Select(MapAccountDto).ToList(),
        Ui = new UiDto
        {
            TimezonePrimary = config.Ui.TimezonePrimary,
            TimezoneSecondary = config.Ui.TimezoneSecondary,
        },
        System = new SystemDto
        {
            WeatherLocation = config.System.WeatherLocation,
            WeatherProvider = config.System.WeatherProvider,
        },
        Storage = new StorageDto
        {
            DbPath = config.Storage.DbPath,
            AttachmentDownloadDir = config.Storage.AttachmentDownloadDir,
            BufferMonthsBack = config.Storage.BufferMonthsBack,
            BufferMonthsForward = config.Storage.BufferMonthsForward,
        },
        Server = new ServerDto
        {
            ListenAddress = config.Server.ListenAddress,
            RestPort = config.Server.RestPort,
            WsPort = config.Server.WsPort,
        },
    };

    private static AccountDto MapAccountDto(AccountConfig account) => new()
    {
        Id = account.Id,
        Type = FormatAccountType(account.Type),
        DisplayName = account.DisplayName,
        ImapHost = account.ImapHost,
        ImapPort = account.ImapPort,
        ImapTls = account.ImapTls,
        SmtpHost = account.SmtpHost,
        SmtpPort = account.SmtpPort,
        Username = account.Username,
        ClientId = account.ClientId,
        ClientSecret = account.ClientSecret,
        TenantId = account.TenantId,
        Calendars = account.Calendars?.Select(c => new CalendarDto
        {
            Id = c.Id,
            Type = FormatCalendarType(c.Type),
            Url = c.Url,
        }).ToList(),
        IgnoreSslErrors = account.IgnoreSslErrors,
    };

    private static string FormatAccountType(AccountType type) => type switch
    {
        AccountType.Imap => "imap",
        AccountType.Google => "google",
        AccountType.Office365 => "office365",
        AccountType.CalDav => "caldav",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Unknown account type: {type}"),
    };

    private static string FormatCalendarType(CalendarType type) => type switch
    {
        CalendarType.CalDav => "caldav",
        CalendarType.Google => "google",
        CalendarType.Office365 => "office365",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Unknown calendar type: {type}"),
    };
}

// DTOs that mirror ConfigLoader's private DTOs for YAML serialization.
// YamlMember Order attributes enforce property order matching config.example.yaml.

internal sealed class PimConfigDto
{
    [YamlMember(Order = 0)]
    public List<AccountDto>? Accounts { get; set; }

    [YamlMember(Order = 1)]
    public UiDto? Ui { get; set; }

    [YamlMember(Order = 2)]
    public SystemDto? System { get; set; }

    [YamlMember(Order = 3)]
    public StorageDto? Storage { get; set; }

    [YamlMember(Order = 4)]
    public ServerDto? Server { get; set; }
}

internal sealed class AccountDto
{
    [YamlMember(Order = 0)]
    public string? Id { get; set; }

    [YamlMember(Order = 1)]
    public string? Type { get; set; }

    [YamlMember(Order = 2)]
    public string? DisplayName { get; set; }

    [YamlMember(Order = 3)]
    public string? ImapHost { get; set; }

    [YamlMember(Order = 4)]
    public int? ImapPort { get; set; }

    [YamlMember(Order = 5)]
    public bool? ImapTls { get; set; }

    [YamlMember(Order = 6)]
    public string? SmtpHost { get; set; }

    [YamlMember(Order = 7)]
    public int? SmtpPort { get; set; }

    [YamlMember(Order = 8)]
    public string? Username { get; set; }

    [YamlMember(Order = 9)]
    public string? ClientId { get; set; }

    [YamlMember(Order = 10)]
    public string? ClientSecret { get; set; }

    [YamlMember(Order = 11)]
    public string? TenantId { get; set; }

    [YamlMember(Order = 12)]
    public List<CalendarDto>? Calendars { get; set; }

    [YamlMember(Order = 13)]
    public bool? IgnoreSslErrors { get; set; }
}

internal sealed class CalendarDto
{
    [YamlMember(Order = 0)]
    public string? Id { get; set; }

    [YamlMember(Order = 1)]
    public string? Type { get; set; }

    [YamlMember(Order = 2)]
    public string? Url { get; set; }
}

internal sealed class UiDto
{
    [YamlMember(Order = 0)]
    public string? TimezonePrimary { get; set; }

    [YamlMember(Order = 1)]
    public string? TimezoneSecondary { get; set; }
}

internal sealed class SystemDto
{
    [YamlMember(Order = 0)]
    public string? WeatherLocation { get; set; }

    [YamlMember(Order = 1)]
    public string? WeatherProvider { get; set; }
}

internal sealed class StorageDto
{
    [YamlMember(Order = 0)]
    public string? DbPath { get; set; }

    [YamlMember(Order = 1)]
    public string? AttachmentDownloadDir { get; set; }

    [YamlMember(Order = 2)]
    public int? BufferMonthsBack { get; set; }

    [YamlMember(Order = 3)]
    public int? BufferMonthsForward { get; set; }
}

internal sealed class ServerDto
{
    [YamlMember(Order = 0)]
    public string? ListenAddress { get; set; }

    [YamlMember(Order = 1)]
    public int? RestPort { get; set; }

    [YamlMember(Order = 2)]
    public int? WsPort { get; set; }
}
