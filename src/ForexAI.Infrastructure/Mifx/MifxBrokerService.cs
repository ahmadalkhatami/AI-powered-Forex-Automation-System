using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using ForexAI.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Mifx;

public class MifxBrokerService : IBrokerService
{
    private readonly MifxPriceFeed _feed;
    private readonly MifxCommandQueue _queue;
    private readonly ILogger<MifxBrokerService> _logger;

    public MifxBrokerService(
        MifxPriceFeed feed,
        MifxCommandQueue queue,
        ILogger<MifxBrokerService> logger)
    {
        _feed   = feed;
        _queue  = queue;
        _logger = logger;
    }

    public bool IsLive => _feed.IsConnected;

    public Task<BrokerAccountInfo> GetAccountAsync()
    {
        var tick          = _feed.Latest;
        var balance       = tick?.AccountBalance ?? 0m;
        var equity        = tick?.AccountEquity  ?? 0m;
        var unrealizedPnl = (balance > 0 && equity > 0) ? equity - balance : 0m;
        return Task.FromResult(new BrokerAccountInfo(balance, equity, unrealizedPnl));
    }

    public async Task<BrokerOrderResult> PlaceOrderAsync(BrokerOrderRequest request)
    {
        if (!_feed.IsConnected)
        {
            _logger.LogWarning("MIFX EA tidak terkoneksi — order dibatalkan");
            return BrokerOrderResult.Disconnected();
        }

        var command = new MifxOrderCommand(
            CommandId:  Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            Action:     "OPEN",
            Direction:  request.IsBuy ? "BUY" : "SELL",
            Lots:       request.LotSize,
            StopLoss:   request.StopLoss,
            TakeProfit: request.TakeProfit
        );

        _logger.LogInformation(
            "Mengirim perintah order ke MT5 EA: {Dir} {Pair} lot={Lot} SL={SL} TP={TP} | commandId={Id}",
            command.Direction, request.Instrument, command.Lots,
            command.StopLoss, command.TakeProfit, command.CommandId);

        var result = await _queue.EnqueueAsync(command, timeoutSeconds: 30);

        if (result.Status == "FILLED")
        {
            _logger.LogInformation(
                "Order TERISI di MIFX — orderId={OrderId} price={Price}",
                result.OrderId, result.Price);
            return BrokerOrderResult.Filled($"MIFX-{result.OrderId}");
        }

        if (result.Status == "TIMEOUT")
        {
            _logger.LogWarning("Order TIMEOUT — EA tidak respon dalam 30 detik");
            return BrokerOrderResult.TimedOut();
        }

        _logger.LogWarning(
            "Order GAGAL — status={Status} retcode={Retcode}",
            result.Status, result.Retcode);
        return BrokerOrderResult.Failed(result.Retcode);
    }

    public async Task<BrokerExecutionResult> ClosePositionAsync(
        TradePosition position,
        CancellationToken cancellationToken = default)
    {
        if (!_feed.IsConnected)
            return new BrokerExecutionResult(false, null, "MIFX EA tidak terkoneksi", 0m);

        var externalId = position.ExternalTradeId ?? "";
        if (!externalId.StartsWith("MIFX-", StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(externalId["MIFX-".Length..], out var ticket))
        {
            return new BrokerExecutionResult(false, null, $"Invalid MIFX ticket: {externalId}", 0m);
        }

        var command = new MifxOrderCommand(
            CommandId:  Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            Action:     "CLOSE",
            Direction:  position.Direction.ToString(),
            Lots:       position.LotSize,
            StopLoss:   0m,
            TakeProfit: 0m,
            Ticket:     ticket);

        _logger.LogInformation(
            "Mengirim perintah CLOSE ke MT5 EA: tradeId={TradeId} ticket={Ticket} lot={Lot} | commandId={Id}",
            position.TradeId, ticket, position.LotSize, command.CommandId);

        var result = await _queue.EnqueueAsync(command, timeoutSeconds: 8);

        if (result.Status is "CLOSED" or "FILLED" || result.Retcode == -21)
        {
            _logger.LogInformation(
                "Position CLOSED di MIFX — ticket={Ticket} price={Price}",
                ticket, result.Price);
            return new BrokerExecutionResult(true, externalId, null, result.Price);
        }

        _logger.LogWarning(
            "Close order GAGAL — ticket={Ticket} status={Status} retcode={Retcode}",
            ticket, result.Status, result.Retcode);

        return new BrokerExecutionResult(false, externalId, result.Status, result.Price);
    }
}
