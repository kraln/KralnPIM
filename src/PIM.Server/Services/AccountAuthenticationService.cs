using PIM.Server.Registration;

namespace PIM.Server.Services;

/// <summary>
/// Runs initial provider authentication off the startup path so a slow or hung provider
/// (e.g. dead network on OAuth refresh, unreachable IMAP host) cannot prevent Kestrel
/// from binding its listening sockets. Accounts surface as offline via AccountStatusTracker
/// until their first successful auth.
/// </summary>
public sealed class AccountAuthenticationService : BackgroundService
{
    private readonly ProviderRegistry _registry;
    private readonly AccountStatusTracker _statusTracker;
    private readonly ILogger<AccountAuthenticationService> _logger;

    public AccountAuthenticationService(
        ProviderRegistry registry,
        AccountStatusTracker statusTracker,
        ILogger<AccountAuthenticationService> logger)
    {
        _registry = registry;
        _statusTracker = statusTracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting background account authentication");
        await _registry.AuthenticateAllAsync(_statusTracker, stoppingToken);
        _logger.LogInformation("Background account authentication complete");
    }
}
