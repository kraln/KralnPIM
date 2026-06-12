using PIM.Core;
using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Core.Providers;
using PIM.Server.Services;
using PIM.Sync.CalDav;
using PIM.Sync.EventKit;
using PIM.Sync.Google;
using PIM.Sync.Graph;
using PIM.Sync.Imap;

namespace PIM.Server.Registration;

public sealed record SinkInfo(string AccountId, string CalendarId, ICalendarProvider Provider);

public class ProviderRegistry
{
    private readonly Dictionary<string, IMailProvider> _mailProviders = new();
    private readonly Dictionary<string, List<ICalendarProvider>> _calendarProviders = new();
    private readonly Dictionary<string, SinkInfo> _sinkProviders = new();
    private readonly Dictionary<string, GoogleCredentialManager> _googleCredManagers = new();
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

    public virtual IReadOnlyList<SinkInfo> Sinks => _sinkProviders.Values.ToList();

    public virtual bool IsSink(string accountId, string calendarId) =>
        _sinkProviders.ContainsKey(SinkKey(accountId, calendarId));

    private static string SinkKey(string accountId, string calendarId) => $"{accountId}:{calendarId}";

    public virtual IEnumerable<string> AccountIds =>
        _mailProviders.Keys.Union(_calendarProviders.Keys).Union(_sinkProviders.Values.Select(s => s.AccountId));

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

    internal static TimeSpan PerProviderAuthTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public async Task AuthenticateAllAsync(AccountStatusTracker statusTracker, CancellationToken ct)
    {
        var accountIds = _mailProviders.Keys.Union(_calendarProviders.Keys).ToList();
        var tasks = accountIds.Select(id => AuthenticateAccountAsync(id, statusTracker, ct));
        await Task.WhenAll(tasks);
    }

    private async Task AuthenticateAccountAsync(
        string accountId, AccountStatusTracker statusTracker, CancellationToken ct)
    {
        if (_mailProviders.TryGetValue(accountId, out var mailProvider))
        {
            await AuthenticateOneAsync(accountId, "mail", mailProvider.AuthenticateAsync, statusTracker, ct);
        }

        if (_calendarProviders.TryGetValue(accountId, out var calProviders))
        {
            foreach (var provider in calProviders)
            {
                await AuthenticateOneAsync(accountId, "calendar", provider.AuthenticateAsync, statusTracker, ct);
            }
        }

        // Sink providers that aren't shared with _calendarProviders (CalDav sinks) still need auth.
        foreach (var sink in _sinkProviders.Values)
        {
            var alreadyAuthed = _calendarProviders.GetValueOrDefault(sink.AccountId)?.Contains(sink.Provider) == true;
            if (alreadyAuthed)
                continue;

            try
            {
                _logger.LogInformation("Authenticating free/busy sink provider for {AccountId}/{CalendarId}", sink.AccountId, sink.CalendarId);
                await sink.Provider.AuthenticateAsync(ct);
                statusTracker.MarkOnline(sink.AccountId);
            }
            catch (ReauthorizationRequiredException)
            {
                _logger.LogWarning("Sink {AccountId}/{CalendarId} requires re-authorization", sink.AccountId, sink.CalendarId);
                statusTracker.MarkOffline(sink.AccountId, OfflineReason.AuthRequired);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to authenticate sink provider for {AccountId}/{CalendarId}, marking offline", sink.AccountId, sink.CalendarId);
                statusTracker.MarkOffline(sink.AccountId, OfflineReason.Error);
            }
        }
    }

    private async Task AuthenticateOneAsync(
        string accountId,
        string kind,
        Func<CancellationToken, Task> authenticate,
        AccountStatusTracker statusTracker,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PerProviderAuthTimeout);

        try
        {
            _logger.LogInformation("Authenticating {Kind} provider for {AccountId}", kind, accountId);
            await authenticate(cts.Token);
            statusTracker.MarkOnline(accountId);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timed out authenticating {Kind} provider for {AccountId} after {Timeout}s, marking offline",
                kind, accountId, PerProviderAuthTimeout.TotalSeconds);
            statusTracker.MarkOffline(accountId, OfflineReason.Error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ReauthorizationRequiredException)
        {
            _logger.LogWarning("Account {AccountId} requires re-authorization for {Kind}", accountId, kind);
            statusTracker.MarkOffline(accountId, OfflineReason.AuthRequired);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate {Kind} provider for {AccountId}, marking offline", kind, accountId);
            statusTracker.MarkOffline(accountId, OfflineReason.Error);
        }
    }

    /// <summary>
    /// Starts interactive re-authentication for an account.
    /// Returns the auth URL the user must visit, or null if the account type doesn't need interactive auth.
    /// The returned Task completes when the OAuth callback is received.
    /// </summary>
    public async Task<string?> StartReauthAsync(
        string accountId,
        AccountStatusTracker statusTracker,
        WebSocket.WebSocketBroadcaster broadcaster,
        CancellationToken ct)
    {
        if (_googleCredManagers.TryGetValue(accountId, out var credMgr))
        {
            var urlTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            credMgr.OnAuthUrlNeeded = url => urlTcs.TrySetResult(url);

            // Start the auth flow in a background task
            _ = Task.Run(async () =>
            {
                try
                {
                    var mailProvider = GetMailProvider(accountId);
                    if (mailProvider is not null)
                        await mailProvider.AuthenticateAsync(ct);

                    foreach (var calProvider in GetCalendarProviders(accountId))
                        await calProvider.AuthenticateAsync(ct);

                    statusTracker.MarkOnline(accountId);
                    await broadcaster.BroadcastAsync(
                        new WebSocket.StatusChangeEvent(accountId, true), ct);
                    _logger.LogInformation("Re-authentication succeeded for {AccountId}", accountId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Re-authentication failed for {AccountId}", accountId);
                    urlTcs.TrySetException(ex);
                }
                finally
                {
                    credMgr.OnAuthUrlNeeded = null;
                }
            }, ct);

            // Wait for the auth URL to be produced (or failure)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                return await urlTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        // O365/Graph: device code flow — could be extended similarly
        return null;
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
            case AccountType.EventKit:
                BuildEventKitProviders(account, loggerFactory);
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
        _googleCredManagers[account.Id] = credMgr;

        var rateLimiter = new TokenBucketRateLimiter(250, 250);
        var allowedCalendarIds = account.Calendars?
            .Where(c => c.FreebusySink != true)
            .Select(c => c.Id)
            .ToHashSet();

        _mailProviders[account.Id] = new GoogleMailProvider(
            account.Id, credMgr, syncStateRepo, rateLimiter,
            loggerFactory.CreateLogger<GoogleMailProvider>());

        var calendarProvider = new GoogleCalendarProvider(
            account.Id, credMgr, syncStateRepo, rateLimiter,
            loggerFactory.CreateLogger<GoogleCalendarProvider>(),
            allowedCalendarIds);

        _calendarProviders[account.Id] = [calendarProvider];

        foreach (var sink in account.Calendars?.Where(c => c.FreebusySink == true) ?? [])
            _sinkProviders[SinkKey(account.Id, sink.Id)] = new SinkInfo(account.Id, sink.Id, calendarProvider);
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
        var allowedCalendarIds = account.Calendars?
            .Where(c => c.FreebusySink != true)
            .Select(c => c.Id)
            .ToHashSet();

        _mailProviders[account.Id] = new GraphMailProvider(
            account.Id, graphAuth, syncStateRepo,
            loggerFactory.CreateLogger<GraphMailProvider>());

        var calendarProvider = new GraphCalendarProvider(
            account.Id, graphAuth, syncStateRepo,
            loggerFactory.CreateLogger<GraphCalendarProvider>(),
            allowedCalendarIds);

        _calendarProviders[account.Id] = [calendarProvider];

        foreach (var sink in account.Calendars?.Where(c => c.FreebusySink == true) ?? [])
            _sinkProviders[SinkKey(account.Id, sink.Id)] = new SinkInfo(account.Id, sink.Id, calendarProvider);
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
            account.IgnoreSslErrors == true,
            account.SkipCertificateRevocationCheck == true);

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

                var provider = new CalDavCalendarProvider(
                    account.Id, calConfig.Id, calConfig.Url!,
                    account.Username!, authRepo, syncStateRepo,
                    httpClient, loggerFactory.CreateLogger<CalDavCalendarProvider>());

                if (calConfig.FreebusySink == true)
                    _sinkProviders[SinkKey(account.Id, calConfig.Id)] = new SinkInfo(account.Id, calConfig.Id, provider);
                else
                    calendars.Add(provider);
            }
        }

        if (calendars.Count > 0)
            _calendarProviders[account.Id] = calendars;
    }

    private void BuildEventKitProviders(AccountConfig account, ILoggerFactory loggerFactory)
    {
        var binaryPath = account.EventKitBinaryPath ?? "eventkit-cli";
        var allowedCalendarIds = account.Calendars?
            .Where(c => c.Type == CalendarType.EventKit && c.FreebusySink != true)
            .Select(c => c.Id)
            .ToHashSet();

        var provider = new EventKitCalendarProvider(
            account.Id, binaryPath, allowedCalendarIds, loggerFactory);

        _calendarProviders[account.Id] = [provider];

        foreach (var sink in account.Calendars?.Where(c => c.Type == CalendarType.EventKit && c.FreebusySink == true) ?? [])
            _sinkProviders[SinkKey(account.Id, sink.Id)] = new SinkInfo(account.Id, sink.Id, provider);
    }
}
