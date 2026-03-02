using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PIM.Core.Config;

public static class ConfigLoader
{
    public static PimConfig Load(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        return LoadFromString(yaml);
    }

    public static PimConfig LoadFromString(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var dto = deserializer.Deserialize<PimConfigDto>(yaml)
            ?? throw new ConfigValidationException(["Configuration file is empty or invalid."]);

        var config = MapToConfig(dto);
        Validate(config);
        return config;
    }

    private static PimConfig MapToConfig(PimConfigDto dto)
    {
        var accounts = (dto.Accounts ?? []).Select(MapAccount).ToList();

        var ui = new UiConfig(
            dto.Ui?.TimezonePrimary ?? "UTC",
            dto.Ui?.TimezoneSecondary
        );

        var system = new SystemConfig(
            dto.System?.WeatherLocation,
            dto.System?.WeatherProvider ?? "open-meteo"
        );

        var storage = new StorageConfig(
            dto.Storage?.DbPath ?? "~/.pim/pim.db",
            dto.Storage?.AttachmentDownloadDir ?? "~/Downloads/pim-attachments",
            dto.Storage?.BufferMonthsBack ?? 6,
            dto.Storage?.BufferMonthsForward ?? 6
        );

        var server = new ServerConfig(
            dto.Server?.ListenAddress ?? "127.0.0.1",
            dto.Server?.RestPort ?? 9400,
            dto.Server?.WsPort ?? 9401
        );

        return new PimConfig(accounts, ui, system, storage, server);
    }

    private static AccountConfig MapAccount(AccountDto dto)
    {
        var type = ParseAccountType(dto.Type);
        var calendars = dto.Calendars?.Select(c => new CalendarSourceConfig(
            c.Id ?? "",
            ParseCalendarType(c.Type),
            c.Url
        )).ToList();

        return new AccountConfig(
            dto.Id ?? "",
            type,
            dto.DisplayName ?? "",
            dto.ImapHost,
            dto.ImapPort,
            dto.ImapTls,
            dto.SmtpHost,
            dto.SmtpPort,
            dto.Username,
            dto.ClientId,
            dto.ClientSecret,
            dto.TenantId,
            calendars
        );
    }

    private static AccountType ParseAccountType(string? type) => type?.ToLowerInvariant() switch
    {
        "imap" => AccountType.Imap,
        "google" => AccountType.Google,
        "office365" => AccountType.Office365,
        _ => throw new ConfigValidationException([$"Unknown account type: '{type}'"])
    };

    private static CalendarType ParseCalendarType(string? type) => type?.ToLowerInvariant() switch
    {
        "caldav" => CalendarType.CalDav,
        "google" => CalendarType.Google,
        "office365" => CalendarType.Office365,
        _ => throw new ConfigValidationException([$"Unknown calendar type: '{type}'"])
    };

    private static void Validate(PimConfig config)
    {
        var errors = new List<string>();

        if (config.Accounts.Count == 0)
            errors.Add("At least one account must be configured.");

        var seenIds = new HashSet<string>();
        foreach (var account in config.Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.Id))
            {
                errors.Add("Account 'id' is required.");
                continue;
            }

            if (!seenIds.Add(account.Id))
                errors.Add($"Duplicate account id: '{account.Id}'.");

            if (string.IsNullOrWhiteSpace(account.DisplayName))
                errors.Add($"Account '{account.Id}': 'display_name' is required.");

            switch (account.Type)
            {
                case AccountType.Imap:
                    if (string.IsNullOrWhiteSpace(account.ImapHost))
                        errors.Add($"Account '{account.Id}': 'imap_host' is required for IMAP accounts.");
                    if (account.ImapPort is null or <= 0 or > 65535)
                        errors.Add($"Account '{account.Id}': 'imap_port' must be between 1 and 65535.");
                    if (string.IsNullOrWhiteSpace(account.SmtpHost))
                        errors.Add($"Account '{account.Id}': 'smtp_host' is required for IMAP accounts.");
                    if (account.SmtpPort is null or <= 0 or > 65535)
                        errors.Add($"Account '{account.Id}': 'smtp_port' must be between 1 and 65535.");
                    if (string.IsNullOrWhiteSpace(account.Username))
                        errors.Add($"Account '{account.Id}': 'username' is required for IMAP accounts.");
                    break;

                case AccountType.Google:
                    if (string.IsNullOrWhiteSpace(account.ClientId))
                        errors.Add($"Account '{account.Id}': 'client_id' is required for Google accounts.");
                    if (string.IsNullOrWhiteSpace(account.ClientSecret))
                        errors.Add($"Account '{account.Id}': 'client_secret' is required for Google accounts.");
                    break;

                case AccountType.Office365:
                    if (string.IsNullOrWhiteSpace(account.TenantId))
                        errors.Add($"Account '{account.Id}': 'tenant_id' is required for Office365 accounts.");
                    if (string.IsNullOrWhiteSpace(account.ClientId))
                        errors.Add($"Account '{account.Id}': 'client_id' is required for Office365 accounts.");
                    break;
            }

            if (account.Calendars != null)
            {
                foreach (var cal in account.Calendars)
                {
                    if (string.IsNullOrWhiteSpace(cal.Id))
                        errors.Add($"Account '{account.Id}': calendar 'id' is required.");
                    if (cal.Type == CalendarType.CalDav && string.IsNullOrWhiteSpace(cal.Url))
                        errors.Add($"Account '{account.Id}', calendar '{cal.Id}': 'url' is required for CalDAV calendars.");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(config.Storage.DbPath))
            errors.Add("'storage.db_path' is required.");

        if (config.Server.RestPort is <= 0 or > 65535)
            errors.Add("'server.rest_port' must be between 1 and 65535.");

        if (config.Server.WsPort is <= 0 or > 65535)
            errors.Add("'server.ws_port' must be between 1 and 65535.");

        if (errors.Count > 0)
            throw new ConfigValidationException(errors);
    }

    // Internal DTO classes for YamlDotNet deserialization
    private sealed class PimConfigDto
    {
        public List<AccountDto>? Accounts { get; set; }
        public UiDto? Ui { get; set; }
        public SystemDto? System { get; set; }
        public StorageDto? Storage { get; set; }
        public ServerDto? Server { get; set; }
    }

    private sealed class AccountDto
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? DisplayName { get; set; }
        public string? ImapHost { get; set; }
        public int? ImapPort { get; set; }
        public bool? ImapTls { get; set; }
        public string? SmtpHost { get; set; }
        public int? SmtpPort { get; set; }
        public string? Username { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? TenantId { get; set; }
        public List<CalendarDto>? Calendars { get; set; }
    }

    private sealed class CalendarDto
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Url { get; set; }
    }

    private sealed class UiDto
    {
        public string? TimezonePrimary { get; set; }
        public string? TimezoneSecondary { get; set; }
    }

    private sealed class SystemDto
    {
        public string? WeatherLocation { get; set; }
        public string? WeatherProvider { get; set; }
    }

    private sealed class StorageDto
    {
        public string? DbPath { get; set; }
        public string? AttachmentDownloadDir { get; set; }
        public int? BufferMonthsBack { get; set; }
        public int? BufferMonthsForward { get; set; }
    }

    private sealed class ServerDto
    {
        public string? ListenAddress { get; set; }
        public int? RestPort { get; set; }
        public int? WsPort { get; set; }
    }
}
