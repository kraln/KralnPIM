using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PIM.Tui.Models;

namespace PIM.Tui.Client;

public sealed class PimWsClient : IAsyncDisposable
{
    private readonly Uri _wsUri;
    private readonly ILogger<PimWsClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    public event Action<MailSyncEvent>? OnMailSync;
    public event Action<CalendarSyncEvent>? OnCalendarSync;
    public event Action<StatusChangeEvent>? OnStatusChange;
    public event Action<bool>? OnConnectionStateChanged;

    public PimWsClient(Uri wsUri, ILogger<PimWsClient> logger)
    {
        _wsUri = wsUri;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _jsonOptions.TypeInfoResolverChain.Add(TuiJsonContext.Default);
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await ConnectInternalAsync(_cts.Token);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_wsUri, ct);
        _logger.LogInformation("WebSocket connected to {Uri}", _wsUri);
        OnConnectionStateChanged?.Invoke(true);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var delayMs = 1000;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _ws!.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket closed by server");
                    OnConnectionStateChanged?.Invoke(false);
                    await ReconnectAsync(ct);
                    delayMs = 1000;
                    continue;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    DispatchEvent(json);
                }

                delayMs = 1000;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "WebSocket error, reconnecting in {Delay}ms", delayMs);
                OnConnectionStateChanged?.Invoke(false);
                await ReconnectWithBackoffAsync(delayMs, ct);
                delayMs = Math.Min(delayMs * 2, 30_000);
            }
        }
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        var delayMs = 1000;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delayMs, ct);
                await ConnectInternalAsync(ct);
                return;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnect failed, retrying in {Delay}ms", delayMs);
                delayMs = Math.Min(delayMs * 2, 30_000);
            }
        }
    }

    private async Task ReconnectWithBackoffAsync(int delayMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delayMs, ct);
            await ConnectInternalAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reconnect attempt failed");
        }
    }

    internal void DispatchEvent(string json)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize(json, TuiJsonContext.Default.WsEventEnvelope);
            if (envelope is null) return;

            switch (envelope.Type)
            {
                case "mail.sync":
                    var mailEvt = JsonSerializer.Deserialize(json, TuiJsonContext.Default.MailSyncEvent);
                    if (mailEvt is not null) OnMailSync?.Invoke(mailEvt);
                    break;

                case "calendar.sync":
                    var calEvt = JsonSerializer.Deserialize(json, TuiJsonContext.Default.CalendarSyncEvent);
                    if (calEvt is not null) OnCalendarSync?.Invoke(calEvt);
                    break;

                case "status.change":
                    var statusEvt = JsonSerializer.Deserialize(json, TuiJsonContext.Default.StatusChangeEvent);
                    if (statusEvt is not null) OnStatusChange?.Invoke(statusEvt);
                    break;

                default:
                    _logger.LogDebug("Unknown WebSocket event type: {Type}", envelope.Type);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse WebSocket event");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();

            if (_receiveLoop is not null)
            {
                try { await _receiveLoop; }
                catch (OperationCanceledException) { }
            }

            _cts.Dispose();
        }

        if (_ws is not null)
        {
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "TUI shutting down",
                        CancellationToken.None);
                }
                catch { /* best effort */ }
            }

            _ws.Dispose();
        }
    }
}
