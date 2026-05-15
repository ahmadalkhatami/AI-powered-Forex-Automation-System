using System.Collections.Concurrent;

namespace ForexAI.Infrastructure.Mifx;

public record MifxOrderCommand(
    string CommandId,
    string Action,      // "OPEN" | "CLOSE"
    string Direction,   // "BUY" | "SELL"
    decimal Lots,
    decimal StopLoss,
    decimal TakeProfit,
    long? Ticket = null
);

public record MifxOrderResult(
    string CommandId,
    string Status,      // "FILLED" | "FAILED"
    string? OrderId,
    decimal Price,
    int Retcode
);

/// <summary>
/// Singleton — antrian perintah order dari backend ke EA MT5.
/// EA polling setiap detik → dequeue → execute → report result.
/// </summary>
public class MifxCommandQueue
{
    private readonly ConcurrentQueue<MifxOrderCommand> _pending = new();

    private readonly ConcurrentDictionary<string, TaskCompletionSource<MifxOrderResult>>
        _awaiting = new();

    // Idempotency cache — commandIds yang sudah pernah complete dalam window 5 menit.
    // Mencegah EA double-POST /order-result (network retry) jadi double-fill di domain logic.
    private readonly ConcurrentDictionary<string, (MifxOrderResult Result, DateTimeOffset At)>
        _recentResults = new();
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Backend enqueue order → menunggu hasil dari EA (max timeout detik).
    /// </summary>
    public Task<MifxOrderResult> EnqueueAsync(MifxOrderCommand command, int timeoutSeconds = 30)
    {
        var tcs = new TaskCompletionSource<MifxOrderResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _awaiting[command.CommandId] = tcs;
        _pending.Enqueue(command);

        // Auto-cancel jika EA tidak merespons dalam batas waktu
        _ = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)).ContinueWith(_ =>
        {
            if (_awaiting.TryRemove(command.CommandId, out var t))
                t.TrySetResult(new MifxOrderResult(command.CommandId, "TIMEOUT", null, 0, -1));
        });

        return tcs.Task;
    }

    /// <summary>
    /// EA memanggil endpoint GET /api/mifx/command → dequeue command.
    /// </summary>
    public MifxOrderCommand? Dequeue()
        => _pending.TryDequeue(out var command) ? command : null;

    /// <summary>
    /// EA memanggil endpoint POST /api/mifx/order-result → selesaikan task.
    /// Idempotent: kalau commandId sudah pernah di-complete dalam 5 menit terakhir,
    /// return DUPLICATE (caller bisa skip double-processing).
    /// </summary>
    public CompleteOutcome Complete(MifxOrderResult result)
    {
        // Garbage-collect entry lama (lazy GC)
        var cutoff = DateTimeOffset.UtcNow - IdempotencyWindow;
        foreach (var kv in _recentResults)
            if (kv.Value.At < cutoff)
                _recentResults.TryRemove(kv.Key, out _);

        if (_recentResults.ContainsKey(result.CommandId))
            return CompleteOutcome.Duplicate;

        _recentResults[result.CommandId] = (result, DateTimeOffset.UtcNow);

        if (_awaiting.TryRemove(result.CommandId, out var tcs))
        {
            tcs.TrySetResult(result);
            return CompleteOutcome.Completed;
        }
        return CompleteOutcome.Orphaned;  // result datang tapi tidak ada task awaiting
    }
}

public enum CompleteOutcome
{
    Completed,    // task awaiting → result delivered
    Duplicate,    // commandId pernah complete dalam window — skip
    Orphaned      // result datang tapi tidak ada task awaiting (kemungkinan timeout)
}
