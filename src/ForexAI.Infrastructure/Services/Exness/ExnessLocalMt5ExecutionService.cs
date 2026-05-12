using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services.Exness;

public class ExnessLocalMt5ExecutionService : ITradeExecutionService
{
    private readonly Mt5CommandBus _bus;
    private readonly ILogger<ExnessLocalMt5ExecutionService> _logger;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public ExnessLocalMt5ExecutionService(Mt5CommandBus bus, ILogger<ExnessLocalMt5ExecutionService> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    public async Task<BrokerAccountStatus> GetAccountStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting account status from MT5...");

        // We send a lightweight FETCH_DATA command to get equity info alongside candles.
        // MT5 always includes account info in every callback.
        var payload = new { Symbol = "EURUSD", Timeframe = "M1", Count = 1 };
        var result = await _bus.SendAsync("FETCH_DATA", payload, Timeout, cancellationToken);

        if (!result.Success)
            throw new Exception($"MT5 account status error: {result.Error}");

        return new BrokerAccountStatus(
            "EXNESS-LOCAL",
            (decimal)result.Equity,
            (decimal)result.Balance,
            (decimal)result.MarginUsed,
            (decimal)result.MarginFree,
            result.OpenPositionCount
        );
    }

    public async Task<BrokerExecutionResult> ExecuteOrderAsync(
        string pair, string direction, decimal lotSize,
        decimal? stopLoss, decimal? takeProfit,
        CancellationToken cancellationToken = default)
    {
        var symbol = pair.Replace("/", "");
        _logger.LogInformation("Sending EXECUTE_TRADE to MT5: {Dir} {Symbol} Lot={Lot}", direction, symbol, lotSize);

        var payload = new
        {
            Symbol = symbol,
            Direction = direction.ToUpper(),
            LotSize = (double)lotSize,
            StopLoss = (double)(stopLoss ?? 0),
            TakeProfit = (double)(takeProfit ?? 0)
        };

        var result = await _bus.SendAsync("EXECUTE_TRADE", payload, Timeout, cancellationToken);

        if (!result.Success)
            return new BrokerExecutionResult(false, null, result.Error, 0m);

        return new BrokerExecutionResult(true, result.OrderId ?? $"EX-{DateTime.UtcNow.Ticks}", null, (decimal)result.ExecutedPrice);
    }
}
