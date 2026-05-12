using ForexAI.Infrastructure.Mifx;
using Xunit;

namespace ForexAI.Integration;

public class MifxCommandQueueTests
{
    [Fact]
    public void Dequeue_ReturnsCommandsInFifoOrder()
    {
        var queue = new MifxCommandQueue();
        _ = queue.EnqueueAsync(new MifxOrderCommand("OPEN1", "OPEN", "BUY", 0.01m, 1.1000m, 1.1100m), 5);
        _ = queue.EnqueueAsync(new MifxOrderCommand("CLOSE1", "CLOSE", "BUY", 0.01m, 0m, 0m, 123456), 5);

        var first = queue.Dequeue();
        var second = queue.Dequeue();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("OPEN1", first.CommandId);
        Assert.Equal("CLOSE1", second.CommandId);
        Assert.Equal("CLOSE", second.Action);
        Assert.Equal(123456, second.Ticket);
        Assert.Null(queue.Dequeue());
    }

    [Fact]
    public async Task Complete_ResolvesCloseCommandResult()
    {
        var queue = new MifxCommandQueue();
        var resultTask = queue.EnqueueAsync(
            new MifxOrderCommand("CLOSE1", "CLOSE", "SELL", 0.02m, 0m, 0m, 987654),
            5);

        queue.Complete(new MifxOrderResult("CLOSE1", "CLOSED", "987654", 1.08765m, 10009));

        var result = await resultTask;

        Assert.Equal("CLOSED", result.Status);
        Assert.Equal("987654", result.OrderId);
        Assert.Equal(1.08765m, result.Price);
    }
}
