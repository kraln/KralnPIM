using System.Text.Json.Serialization;

namespace PIM.Core.Config;

[JsonConverter(typeof(JsonStringEnumConverter<AccountType>))]
public enum AccountType { Imap, Google, Office365, CalDav }

[JsonConverter(typeof(JsonStringEnumConverter<CalendarType>))]
public enum CalendarType { CalDav, Google, Office365 }

public sealed record PimConfig(
    List<AccountConfig> Accounts,
    UiConfig Ui,
    SystemConfig System,
    StorageConfig Storage,
    ServerConfig Server
);

public sealed record AccountConfig(
    string Id,
    AccountType Type,
    string DisplayName,
    string? ImapHost,
    int? ImapPort,
    bool? ImapTls,
    string? SmtpHost,
    int? SmtpPort,
    string? Username,
    string? ClientId,
    string? ClientSecret,
    string? TenantId,
    List<CalendarSourceConfig>? Calendars,
    bool? IgnoreSslErrors = null,
    string? CalDavUrl = null
);

public sealed record CalendarSourceConfig(
    string Id,
    CalendarType Type,
    string? Url
);

public sealed record UiConfig(
    string TimezonePrimary,
    string? TimezoneSecondary
);

public sealed record SystemConfig(
    string? WeatherLocation,
    string WeatherProvider
);

public sealed record StorageConfig(
    string DbPath,
    string AttachmentDownloadDir,
    int BufferMonthsBack,
    int BufferMonthsForward
);

public sealed record ServerConfig(
    string ListenAddress,
    int RestPort,
    int WsPort
);
