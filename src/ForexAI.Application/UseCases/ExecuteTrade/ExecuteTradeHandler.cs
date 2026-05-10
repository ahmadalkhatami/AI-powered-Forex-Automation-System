using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ForexAI.Application.UseCases.ExecuteTrade;

public class ExecuteTradeHandler : IRequestHandler<ExecuteTradeCommand, TradePosition>
{
    private const int MaxOpenPositions = 3;
    private const decimal MaxDrawdownPct = 0.10m;
    private const decimal MaxRiskPerTradePct = 0.01m;

    private readonly ISignalRepository _signals;
    private readonly ITradePositionRepository _positions;
    private readonly ILogger<ExecuteTradeHandler> _logger;

    public ExecuteTradeHandler(
        ISignalRepository signals,
        ITradePositionRepository positions,
        ILogger<ExecuteTradeHandler> logger)
    {
        _signals = signals;
        _positions = positions;
        _logger = logger;
    }

    public async Task<TradePosition> Handle(ExecuteTradeCommand request, CancellationToken cancellationToken)
    {
        var signal = await _signals.GetByIdAsync(request.SignalId)
            ?? throw new InvalidOperationException($"Signal {request.SignalId} not found");

        var tradeId = $"SIM-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";

        // First Law: never execute without GO
        if (!request.RiskValidation.IsGo)
        {
            var skipReason = string.Join("; ", request.RiskValidation.NoGoReasons);
            _logger.LogWarning("Trade skipped (NO-GO): {Reason}", skipReason);

            var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, skipReason);
            await _positions.SaveAsync(skipped);
            return skipped;
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
        var drawdown = (request.PeakEquity - request.CurrentEquity) / request.PeakEquity;
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
        var actualRiskPct = p.RiskAmount / request.CurrentEquity;
        if (actualRiskPct > MaxRiskPerTradePct)
        {
            var msg = $"Risk {actualRiskPct:P2} exceeds 1% limit";
            _logger.LogWarning("Trade skipped: {Reason}", msg);
            var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
            await _positions.SaveAsync(skipped);
            return skipped;
        }

        var position = TradePosition.CreateSimulated(
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

        await _positions.SaveAsync(position);

        _logger.LogInformation(
            "Trade OPEN [{Mode}] {TradeId} — {Direction} {Pair} @ {Entry}, SL {SL}, TP {TP}, lot {Lot}",
            request.Mode, tradeId, signal.Signal, signal.Pair,
            p.Entry, p.StopLoss, p.TakeProfit, p.LotSize);

        return position;
    }
}
