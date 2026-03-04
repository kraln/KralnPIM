using PIM.Core;
using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Core.Providers;
using PIM.Sync.CalDav;
using PIM.Sync.Google;
using PIM.Sync.Graph;
using PIM.Sync.Imap;

namespace PIM.Server.Registration;

public class ProviderRegistry
{
    private readonly Dictionary<string, IMailProvider> _mailProviders = new();
    private readonly Dictionary<string, List<ICalendarProvider>> _calendarProviders = new();
    private readonly ILogger<ProviderRegistry> _logger;

    public ProviderRegistry(ILogger<ProviderRegistry> logger)
    {
        _logger = logger;
    }

    public virtual IMailProvider? GetMailProvider(string accountId) =>
        _mailProviders.GetValueOrDefault(accountId);

    public virtual List<ICalendarProvider> GetCalendarProviders(string accountId) =>
        _calendarProviders.GetValueOrDefault(accountId) ?? [];

    public virtual IReadOnlyList<IMailProvider> AllMailProviders => _mailProviders.Values.ToList();

    public virtual IReadOnlyList<ICalendarProvider> AllCalendarProviders =>
        _calendarProviders.Values.SelectMany(p => p).ToList();

    public virtual IEnumerable<string> AccountIds => _mailProviders.Keys.Union(_calendarProviders.Keys);

    public async Task InitializeAsync(
        PimConfig config,
        IAuthRepository authRepo,
        ISyncStateRepository syncStateRepo,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        foreach (var account in config.Accounts)
        {
            try
            {
                await BuildProvidersForAccountAsync(
                    account, authRepo, syncStateRepo, loggerFactory, httpClientFactory, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build providers for account {AccountId}", account.Id);
            }
        }
    }

    public async Task AuthenticateAllAsync(CancellationToken ct)
    {
        foreach (var (accountId, provider) in _mailProviders)
        {
            _logger.LogInformation("Authenticating mail provider for {AccountId}", accountId);
            await provider.AuthenticateAsync(ct);
        }

        foreach (var (accountId, providers) in _calendarProviders)
        {
            foreach (var provider in providers)
            {
                _logger.LogInformation("Authenticating calendar provider for {AccountId}", accountId);
                await provider.AuthenticateAsync(ct);
            }
        }
    }

    private async Task BuildProvidersForAccountAsync(
        AccountConfig account,
        IAuthRepository authRepo,
        ISyncStateRepository syncStateRepo,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        switch (account.Type)
        {
            case AccountType.Google:
                BuildGoogleProviders(account, authRepo, syncStateRepo, loggerFactory);
                break;
            case AccountType.Office365:
                BuildGraphProviders(account, authRepo, syncStateRepo, loggerFactory);
                break;
            case AccountType.Imap:
                await BuildImapProvidersAsync(account, authRepo, syncStateRepo, loggerFactory, httpClientFactory, ct);
                break;
            case AccountType.CalDav:
                await BuildCalDavProvidersAsync(account, authRepo, syncStateRepo, loggerFactory, httpClientFactory, ct);
                break;
        }
    }

    private void BuildGoogleProviders(
        AccountConfig account,
        IAuthRepository authRepo,
        ISyncStateRepository syncStateRepo,
        ILoggerFactory loggerFactory)
    {
        var clientId = account.ClientId ?? DefaultCredentials.Google.ClientId;
        var clientSecret = account.ClientSecret ?? DefaultCredentials.Google.ClientSecret;
        var credMgr = new GoogleCredentialManager(
            account.Id, clientId, clientSecret,
            authRepo, loggerFactory.CreateLogger<GoogleCredentialManager>());

        var rateLimiter = new TokenBucketRateLimiter(250, 250);
        var allowedCalendarIds = account.Calendars?.Select(c => c.Id).ToHashSet();

        _mailProviders[account.Id] = new GoogleMailProvider(
            account.Id, credMgr, syncStateRepo, rateLimiter,
            loggerFactory.CreateLogger<GoogleMailProvider>());

        _calendarProviders[account.Id] = [
            new GoogleCalendarProvider(
                account.Id, credMgr, syncStateRepo, rateLimiter,
                loggerFactory.CreateLogger<GoogleCalendarProvider>(),
                allowedCalendarIds)
        ];
    }

    private void BuildGraphProviders(
        AccountConfig account,
        IAuthRepository authRepo,
        ISyncStateRepository syncStateRepo,
        ILoggerFactory loggerFactory)
    {
        var o365ClientId = account.ClientId ?? DefaultCredentials.Office365.ClientId;
        var o365TenantId = account.TenantId ?? DefaultCredentials.Office365.TenantId;
        var graphAuth = new GraphAuthProvider(
            account.Id, o365ClientId, o365TenantId,
            authRepo, loggerFactory.CreateLogger<GraphAuthProvider>());
        var allowedCalendarIds = account.Calendars?.Select(c => c.Id).ToHashSet();

        _mailProviders[account.Id] = new GraphMailProvider(
            account.Id, graphAuth, syncStateRepo,
            loggerFactory.CreateLogger<GraphMailProvider>());

        _calendarProviders[account.Id] = [
            new GraphCalendarProvider(
                account.Id, graphAuth, syncStateRepo,
                loggerFactory.CreateLogger<GraphCalendarProvider>(),
                allowedCalendarIds)
        ];
    }

    private async Task BuildImapProvidersAsync(
        AccountConfig account,
        IAuthRepository authRepo,
        ISyncStateRepository syncStateRepo,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var password = await authRepo.GetImapPasswordAsync(account.Id, ct);
        if (password is null)
        {
            _logger.LogWarning("No password found for IMAP account {AccountId}, skipping", account.Id);
            return;
        }

        var connMgr = new ImapConnectionManager(
            account.ImapHost!, account.ImapPort!.Value, account.ImapTls ?? true,
            account.SmtpHost!, account.SmtpPort!.Value,
            account.Username!, password,
            loggerFactory.CreateLogger<ImapConnectionManager>(),
            account.IgnoreSslErrors == true);

        _mailProviders[account.Id] = new ImapMailProvider(
            account.Id, connMgr, syncStateRepo,
            loggerFactory.CreateLogger<ImapMailProvider>());
    }

    private async Task BuildCalDavProvidersAsync(
        AccountConfig account,
        IAuthRepository authRepo,
        ISyncStateRepository syncStateRepo,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var password = await authRepo.GetCalDavPasswordAsync(account.Id, ct);
        if (password is null)
        {
            _logger.LogWarning("No password found for CalDAV account {AccountId}, skipping", account.Id);
            return;
        }

        var calendars = new List<ICalendarProvider>();
        foreach (var calConfig in account.Calendars ?? [])
        {
            if (calConfig.Type == CalendarType.CalDav)
            {
                HttpClient httpClient;
                if (account.IgnoreSslErrors == true)
                {
                    var handler = new SocketsHttpHandler
                    {
                        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                        {
                            RemoteCertificateValidationCallback = (_, _, _, _) => true,
                        }
                    };
                    httpClient = new HttpClient(handler);
                }
                else
                {
                    httpClient = httpClientFactory.CreateClient($"caldav-{calConfig.Id}");
                }

                calendars.Add(new CalDavCalendarProvider(
                    account.Id, calConfig.Id, calConfig.Url!,
                    account.Username!, authRepo, syncStateRepo,
                    httpClient, loggerFactory.CreateLogger<CalDavCalendarProvider>()));
            }
        }

        if (calendars.Count > 0)
            _calendarProviders[account.Id] = calendars;
    }
}
