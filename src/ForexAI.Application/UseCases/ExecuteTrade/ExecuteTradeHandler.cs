using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ForexAI.Application.UseCases.ExecuteTrade;

public class ExecuteTradeHandler : IRequestHandler<ExecuteTradeCommand, TradePosition>
{
    private const int MaxOpenPositions = 3;
    private const decimal MaxDrawdownPct = 0.10m;
    // 1% target + 5 basis-point tolerance untuk floating point dan
    // equity drift kecil antara saat risk evaluation dan saat execute.
    private const decimal MaxRiskPerTradePct = 0.0105m;

    private readonly ISignalRepository _signals;
    private readonly ITradePositionRepository _positions;
    private readonly IBrokerService _broker;
    private readonly ILogger<ExecuteTradeHandler> _logger;

    public ExecuteTradeHandler(
        ISignalRepository signals,
        ITradePositionRepository positions,
        IBrokerService broker,
        ILogger<ExecuteTradeHandler> logger)
    {
        _signals = signals;
        _positions = positions;
        _broker = broker;
        _logger = logger;
    }

    public async Task<TradePosition> Handle(ExecuteTradeCommand request, CancellationToken cancellationToken)
    {
        var signal = await _signals.GetByIdAsync(request.SignalId)
            ?? throw new InvalidOperationException($"Signal {request.SignalId} not found");

        var tradePrefix = _broker.IsLive ? "MIFX" : "SIM";
        var tradeId = $"{tradePrefix}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";

        // First Law: never execute without GO
        if (!request.RiskValidation.IsGo)
        {
            var skipReason = string.Join("; ", request.RiskValidation.NoGoReasons);
            _logger.LogWarning("Trade skipped (NO-GO): {Reason}", skipReason);

            var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, skipReason);
            await _positions.SaveAsync(skipped);
            return skipped;
        }

        // Fetch live equity from broker when connected, otherwise use command values
        decimal currentEquity = request.CurrentEquity;
        decimal peakEquity = request.PeakEquity;
        if (_broker.IsLive)
        {
            var account = await _broker.GetAccountAsync();
            currentEquity = account.Equity;
            // Use peak equity from command unless not provided (0 = caller defers to broker)
            if (peakEquity == 0m) peakEquity = account.Equity;
        }

        // Hard limit: max open positions
        var openCount = await _positions.CountOpenPositionsAsync();
        if (openCount >= MaxOpenPositions)
        {
            var msg = $"Max open positions reached ({MaxOpenPositions})";
            _logger.LogWarning("Trade skipped: {Reason}", msg);
            var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
            await _positions.SaveAsync(skipped);
            return skipped;
        }

        // Hard limit: max drawdown 10%
        var drawdown = peakEquity > 0 ? (peakEquity - currentEquity) / peakEquity : 0m;
        if (drawdown >= MaxDrawdownPct)
        {
            var msg = $"System STOP — drawdown {drawdown:P1} >= {MaxDrawdownPct:P0} limit";
            _logger.LogCritical(msg);
            var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
            await _positions.SaveAsync(skipped);
            return skipped;
        }

        // Hard limit: risk per trade max 1%
        var p = request.RiskValidation.ValidatedParameters!;
        var actualRiskPct = currentEquity > 0 ? p.RiskAmount / currentEquity : p.RiskAmount;
        if (actualRiskPct > MaxRiskPerTradePct)
        {
            var msg = $"Risk {actualRiskPct:P2} exceeds 1% hard limit (equity drift check)";
            _logger.LogWarning("Trade skipped: {Reason}", msg);
            var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
            await _positions.SaveAsync(skipped);
            return skipped;
        }

        // Guard: HOLD tidak punya arah trade yang valid → skip sebelum kirim ke broker.
        // Jika dikirim ke broker, HOLD dianggap SELL (IsBuy=false) tapi SL/TP-nya
        // dihitung sebagai BUY (SL di bawah entry) → MT5 retcode 10016.
        if (signal.Signal == SignalDirection.HOLD)
        {
            var msg = "Signal HOLD — tidak ada arah trade yang dapat dieksekusi ke broker";
            _logger.LogWarning("Trade skipped: {Reason}", msg);
            var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
            await _positions.SaveAsync(skipped);
            return skipped;
        }

        TradePosition position;

        if (_broker.IsLive)
        {
            var orderReq = new BrokerOrderRequest(
                Instrument: signal.Pair,
                IsBuy: signal.Signal == SignalDirection.BUY,
                LotSize: p.LotSize,
                StopLoss: p.StopLoss,
                TakeProfit: p.TakeProfit);

            // Validasi arah SL sebelum kirim (defensive check)
            bool slOnWrongSide = signal.Signal == SignalDirection.BUY
                ? p.StopLoss >= p.Entry
                : p.StopLoss <= p.Entry;
            if (slOnWrongSide)
            {
                var msg = $"SL {p.StopLoss:F5} ada di sisi salah untuk {signal.Signal} (entry {p.Entry:F5}) — dibatalkan sebelum kirim ke broker";
                _logger.LogError("Trade aborted (invalid SL direction): {Reason}", msg);
                var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
                await _positions.SaveAsync(skipped);
                return skipped;
            }

            _logger.LogInformation(
                "Mengirim ke broker: {Dir} {Pair} entry={Entry} SL={SL}({SLpip}pip) TP={TP}({TPpip}pip) lot={Lot}",
                signal.Signal, signal.Pair, p.Entry, p.StopLoss, p.StopLossPips, p.TakeProfit, p.TakeProfitPips, p.LotSize);

            var brokerResult = await _broker.PlaceOrderAsync(orderReq);

            if (!brokerResult.Success)
            {
                var msg = brokerResult.ErrorMessage ?? $"Broker rejected order ({brokerResult.StatusReason})";
                _logger.LogWarning("Trade skipped: {Reason} (retcode={Code})",
                    msg, brokerResult.BrokerRetcode);
                var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
                await _positions.SaveAsync(skipped);
                return skipped;
            }

            position = TradePosition.CreateBrokerExecuted(
                tradeId,
                signal.RunId,
                signal.Pair,
                signal.Signal,
                p.Entry,
                p.StopLoss,
                p.TakeProfit,
                p.LotSize,
                p.RiskAmount,
                p.PotentialProfit,
                p.RiskRewardRatio,
                mode: "MIFX_DEMO",
                externalTradeId: brokerResult.ExternalId);

            _logger.LogInformation(
                "Trade OPEN [MIFX_DEMO] {TradeId} (ext:{ExternalId}) — {Direction} {Pair} @ {Entry}, SL {SL}, TP {TP}, lot {Lot}",
                tradeId, brokerResult.ExternalId, signal.Signal, signal.Pair,
                p.Entry, p.StopLoss, p.TakeProfit, p.LotSize);
        }
        else
        {
            position = TradePosition.CreateSimulated(
                tradeId,
                signal.RunId,
                signal.Pair,
                signal.Signal,
                p.Entry,
                p.StopLoss,
                p.TakeProfit,
                p.LotSize,
                p.RiskAmount,
                p.PotentialProfit,
                p.RiskRewardRatio);

            _logger.LogInformation(
                "Trade OPEN [SIMULATION] {TradeId} — {Direction} {Pair} @ {Entry}, SL {SL}, TP {TP}, lot {Lot}",
                tradeId, signal.Signal, signal.Pair,
                p.Entry, p.StopLoss, p.TakeProfit, p.LotSize);
        }

        await _positions.SaveAsync(position);
        return position;
    }
}
