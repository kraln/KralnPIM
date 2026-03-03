using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using PIM.Server.Models;

namespace PIM.Server.WebSocket;

public sealed class WebSocketBroadcaster
{
    private readonly ConcurrentDictionary<Guid, System.Net.WebSockets.WebSocket> _clients = new();
    private readonly ILogger<WebSocketBroadcaster> _logger;

    public WebSocketBroadcaster(ILogger<WebSocketBroadcaster> logger)
    {
        _logger = logger;
    }

    public async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket ws, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        _clients.TryAdd(id, ws);
        _logger.LogDebug("WebSocket client {ClientId} connected, total: {Count}", id, _clients.Count);

        try
        {
            var buffer = new byte[256];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _clients.TryRemove(id, out _);
            _logger.LogDebug("WebSocket client {ClientId} disconnected, total: {Count}", id, _clients.Count);
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { /* best effort */ }
            }
        }
    }

    public async Task BroadcastAsync(WsEvent evt, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(evt, evt.GetType(), ServerJsonContext.Default);
        var segment = new ArraySegment<byte>(json);

        foreach (var (id, ws) in _clients)
        {
            if (ws.State != WebSocketState.Open)
            {
                _clients.TryRemove(id, out _);
                continue;
            }

            try
            {
                await ws.SendAsync(segment, WebSocketMessageType.Text, true, ct);
            }
            catch
            {
                _clients.TryRemove(id, out _);
            }
        }
    }

    public int ConnectedCount => _clients.Count;
}
