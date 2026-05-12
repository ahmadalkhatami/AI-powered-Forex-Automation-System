using System.Collections.Concurrent;

namespace ForexAI.Infrastructure.Services.Exness;

/// <summary>
/// In-memory command bus between the C# backend and the MT5 EA.
/// The EA polls /api/mt5/poll, picks up a pending command, executes it inside MT5,
/// and posts the result back to /api/mt5/callback.
/// </summary>
public class Mt5CommandBus
{
    private record PendingCommand(string CommandId, string CommandType, object Payload, TaskCompletionSource<Mt5CallbackPayload> Tcs);

    private readonly ConcurrentQueue<PendingCommand> _queue = new();
    private readonly ConcurrentDictionary<string, PendingCommand> _inflight = new();

    // ── Publish a command and wait for the MT5 callback ──────────────────────────
    public async Task<Mt5CallbackPayload> SendAsync(string commandType, object payload, TimeSpan timeout, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var tcs = new TaskCompletionSource<Mt5CallbackPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cmd = new PendingCommand(id, commandType, payload, tcs);

        _queue.Enqueue(cmd);
        _inflight[id] = cmd;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        cts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

        try
        {
            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _inflight.TryRemove(id, out _);
            throw new TimeoutException($"MT5 did not respond to '{commandType}' within {timeout.TotalSeconds}s. Is the EA running?");
        }
    }

    // ── Called by /api/mt5/poll to get the next pending command ──────────────────
    public (string CommandId, string CommandType, object? Payload)? Dequeue()
    {
        if (_queue.TryDequeue(out var cmd))
            return (cmd.CommandId, cmd.CommandType, cmd.Payload);
        return null;
    }

    // ── Called by /api/mt5/callback to resolve the waiting Task ──────────────────
    public bool Complete(string commandId, Mt5CallbackPayload result)
    {
        if (_inflight.TryRemove(commandId, out var cmd))
        {
            cmd.Tcs.TrySetResult(result);
            return true;
        }
        return false;
    }
}

public record Mt5CallbackPayload(
    bool Success,
    string? Error,
    double Equity,
    double Balance,
    double MarginUsed,
    double MarginFree,
    int OpenPositionCount,
    List<Mt5CandleRow> Candles,
    string? Symbol,
    string? Timeframe,
    string? OrderId,
    double ExecutedPrice
);

public record Mt5CandleRow(long Time, double Open, double High, double Low, double Close, long Volume);
