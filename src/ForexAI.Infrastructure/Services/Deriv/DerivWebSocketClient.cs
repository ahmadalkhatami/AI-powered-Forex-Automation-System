using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services.Deriv;

/// <summary>
/// Manages the persistent WebSocket connection to Deriv's API.
/// Registered as Singleton — one shared connection for the app lifetime.
/// </summary>
public class DerivWebSocketClient : IAsyncDisposable
{
    private ClientWebSocket _ws = new();
    private readonly string _appId;
    private readonly string _apiToken;
    private int _reqId = 0;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private CancellationTokenSource _cts = new();
    private bool _authorized = false;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly ILogger<DerivWebSocketClient> _logger;

    // Public app_id for Deriv demo — 1089 is the default for many apps
    public const string DefaultDemoAppId = "1089";

    public DerivWebSocketClient(string apiToken, string appId, ILogger<DerivWebSocketClient> logger)
    {
        _apiToken = apiToken;
        _appId = appId;
        _logger = logger;
    }

    // ── Connect & Authorize ──────────────────────────────────────────────────
    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        await _connectLock.WaitAsync(ct);
        try
        {
            if (_ws.State == WebSocketState.Open && _authorized)
                return;

            // Reset if needed
            if (_ws.State != WebSocketState.None && _ws.State != WebSocketState.Closed)
            {
                _cts.Cancel();
                await Task.Delay(100, CancellationToken.None);
                _ws.Dispose();
                _ws = new ClientWebSocket();
                _cts = new CancellationTokenSource();
                _authorized = false;
            }

            var uri = new Uri($"wss://ws.binaryws.com/websockets/v3?app_id={_appId}");
            _logger.LogInformation("[Deriv] Connecting to {Uri}", uri);
            await _ws.ConnectAsync(uri, ct);

            // Start background reader
            _ = Task.Run(() => ReadLoopAsync(_cts.Token));

            // Authorize
            _logger.LogInformation("[Deriv] Authorizing...");
            var authResult = await SendRawAsync(new { authorize = _apiToken }, ct);

            if (authResult.TryGetProperty("error", out var err))
            {
                var fullError = JsonSerializer.Serialize(err);
                _logger.LogError("[Deriv] Auth failed with error: {FullError}", fullError);
                throw new Exception($"Deriv auth failed: {err.GetProperty("message").GetString()}");
            }

            var login = authResult.TryGetProperty("authorize", out var auth) ? auth.GetProperty("loginid").GetString() : "?";
            _logger.LogInformation("[Deriv] ✅ Authorized as {Login}", login);
            _authorized = true;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    // ── Send a message and wait for its response ─────────────────────────────
    public async Task<JsonElement> SendAsync(object message, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        return await SendRawAsync(message, ct);
    }

    private async Task<JsonElement> SendRawAsync(object message, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _reqId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var json = BuildJson(message, id);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var reg = timeoutCts.Token.Register(() =>
        {
            _pending.TryRemove(id, out _);
            tcs.TrySetCanceled();
        });

        return await tcs.Task;
    }

    private static string BuildJson(object payload, int reqId)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        writer.WriteNumber("req_id", reqId);

        // Inline properties from the payload
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        foreach (var prop in doc.RootElement.EnumerateObject())
            prop.WriteTo(writer);

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── Background read loop ─────────────────────────────────────────────────
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];
        var sb = new StringBuilder();

        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            try
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                using var doc = JsonDocument.Parse(sb.ToString());
                var root = doc.RootElement.Clone();

                if (root.TryGetProperty("req_id", out var reqIdEl))
                {
                    var reqId = reqIdEl.GetInt32();
                    if (_pending.TryRemove(reqId, out var tcs))
                        tcs.TrySetResult(root);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("[Deriv] ReadLoop error: {Msg}", ex.Message);
                _authorized = false;
                break;
            }
        }

        _logger.LogWarning("[Deriv] ReadLoop ended. State={State}", _ws.State);
        _authorized = false;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_ws.State == WebSocketState.Open)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", CancellationToken.None);
        _ws.Dispose();
    }
}
