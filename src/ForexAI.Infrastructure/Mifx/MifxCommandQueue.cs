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
    /// </summary>
    public void Complete(MifxOrderResult result)
    {
        if (_awaiting.TryRemove(result.CommandId, out var tcs))
            tcs.TrySetResult(result);
    }
}
