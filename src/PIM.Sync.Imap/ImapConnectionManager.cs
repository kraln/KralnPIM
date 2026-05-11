using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;

namespace PIM.Sync.Imap;

public sealed class ImapConnectionManager : IAsyncDisposable
{
    private readonly string _imapHost;
    private readonly int _imapPort;
    private readonly bool _imapUseTls;
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _username;
    private readonly string _password;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private ImapClient? _imapClient;
    private int _reconnectAttempt;

    public bool SupportsCondstore { get; private set; }

    private readonly bool _ignoreSslErrors;
    private readonly bool _skipCertificateRevocationCheck;

    public ImapConnectionManager(
        string imapHost, int imapPort, bool imapUseTls,
        string smtpHost, int smtpPort,
        string username, string password,
        ILogger logger,
        bool ignoreSslErrors = false,
        bool skipCertificateRevocationCheck = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(imapHost);
        ArgumentException.ThrowIfNullOrEmpty(smtpHost);
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(password);

        _imapHost = imapHost;
        _imapPort = imapPort;
        _imapUseTls = imapUseTls;
        _smtpHost = smtpHost;
        _smtpPort = smtpPort;
        _username = username;
        _password = password;
        _logger = logger;
        _ignoreSslErrors = ignoreSslErrors;
        _skipCertificateRevocationCheck = skipCertificateRevocationCheck;
    }

    public async Task<ImapClient> GetImapClientAsync(CancellationToken ct)
    {
        if (_imapClient is { IsConnected: true, IsAuthenticated: true })
            return _imapClient;

        await _connectLock.WaitAsync(ct);
        try
        {
            if (_imapClient is { IsConnected: true, IsAuthenticated: true })
                return _imapClient;

            _imapClient?.Dispose();
            _imapClient = new ImapClient();
            if (_ignoreSslErrors)
                _imapClient.ServerCertificateValidationCallback = (_, _, _, _) => true;
            if (_skipCertificateRevocationCheck)
                _imapClient.CheckCertificateRevocation = false;

            var tlsOptions = _imapPort == 993
                ? SecureSocketOptions.SslOnConnect
                : (_imapUseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None);

            await _imapClient.ConnectAsync(_imapHost, _imapPort, tlsOptions, ct);
            await _imapClient.AuthenticateAsync(_username, _password, ct);

            SupportsCondstore = _imapClient.Capabilities.HasFlag(ImapCapabilities.CondStore);
            _reconnectAttempt = 0;

            _logger.LogInformation("IMAP connected to {Host}:{Port}, CONDSTORE={Condstore}",
                _imapHost, _imapPort, SupportsCondstore);

            return _imapClient;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _reconnectAttempt++;
            var delay = CalculateBackoffMs(_reconnectAttempt);

            _logger.LogWarning(ex, "IMAP connection failed (attempt {Attempt}), backing off {Delay}ms",
                _reconnectAttempt, delay);

            await Task.Delay(delay, ct);
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<SmtpClient> CreateSmtpClientAsync(CancellationToken ct)
    {
        var client = new SmtpClient();
        if (_ignoreSslErrors)
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        if (_skipCertificateRevocationCheck)
            client.CheckCertificateRevocation = false;

        var tlsOptions = _smtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(_smtpHost, _smtpPort, tlsOptions, ct);
        await client.AuthenticateAsync(_username, _password, ct);

        return client;
    }

    internal static int CalculateBackoffMs(int attempt)
    {
        var ms = 1000 * (int)Math.Pow(2, attempt - 1);
        return Math.Min(ms, 60_000);
    }

    public async ValueTask DisposeAsync()
    {
        if (_imapClient is not null)
        {
            if (_imapClient.IsConnected)
            {
                try { await _imapClient.DisconnectAsync(true); }
                catch { /* best effort */ }
            }
            _imapClient.Dispose();
        }
        _connectLock.Dispose();
    }
}
