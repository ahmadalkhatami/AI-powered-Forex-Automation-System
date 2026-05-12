using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
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

    public async Task<string?> PlaceOrderAsync(BrokerOrderRequest request)
    {
        if (!_feed.IsConnected)
        {
            _logger.LogWarning("MIFX EA tidak terkoneksi — order dibatalkan");
            return null;
        }

        var command = new MifxOrderCommand(
            CommandId:  Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
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
            return $"MIFX-{result.OrderId}";
        }

        _logger.LogWarning(
            "Order GAGAL — status={Status} retcode={Retcode}",
            result.Status, result.Retcode);
        return null;
    }
}
