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

    private readonly ISignalRepository _signals;
    private readonly ITradePositionRepository _positions;
    private readonly IBrokerService _broker;
    private readonly ISystemStateService _systemState;
    private readonly IModeService _mode;
    private readonly IMarketSpreadGate _spreadGate;
    private readonly ILogger<ExecuteTradeHandler> _logger;

    public ExecuteTradeHandler(
        ISignalRepository signals,
        ITradePositionRepository positions,
        IBrokerService broker,
        ISystemStateService systemState,
        IModeService mode,
        IMarketSpreadGate spreadGate,
        ILogger<ExecuteTradeHandler> logger)
    {
        _signals = signals;
        _positions = positions;
        _broker = broker;
        _systemState = systemState;
        _mode = mode;
        _spreadGate = spreadGate;
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

        // Kill switch — block execute apapun saat system halted
        if (_systemState.IsHalted)
        {
            var msg = $"System HALTED: {_systemState.HaltReason}";
            _logger.LogWarning("Trade skipped: {Reason}", msg);
            var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
            await _positions.SaveAsync(skipped);
            return skipped;
        }

        // ── Spread Gate (live broker only) ───────────────────────────────────
        // 1. Absolute spread > MaxSpreadPips → reject (broker too wide).
        // 2. Spread spike (current > 2.5× rolling avg) → reject (news event /
        //    liquidity drying). Skip kalau warm-up (< 20 samples).
        if (_broker.IsLive)
        {
            decimal currentSpread = _spreadGate.CurrentSpreadPips;
            if (currentSpread > _systemState.MaxSpreadPips)
            {
                var msg = $"Spread {currentSpread:F1}p > max {_systemState.MaxSpreadPips:F1}p — broker too wide, trade skipped.";
                _logger.LogWarning("Trade skipped: {Reason}", msg);
                var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
                await _positions.SaveAsync(skipped);
                return skipped;
            }
            if (_spreadGate.IsSpike(out var spikeCurrent, out var rollingAvg))
            {
                var msg = $"Spread spike: {spikeCurrent:F1}p > 2.5× rolling avg {rollingAvg:F1}p — likely news/liquidity event, trade skipped.";
                _logger.LogWarning("Trade skipped: {Reason}", msg);
                var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
                await _positions.SaveAsync(skipped);
                return skipped;
            }
        }

        // Circuit breaker: stop kalau LOSS berturut-turut mencapai threshold
        var recentClosed = (await _positions.GetAllAsync())
            .Where(rc => rc.ClosedAt.HasValue &&
                        (rc.Status == TradeStatus.CLOSED_WIN || rc.Status == TradeStatus.CLOSED_LOSS))
            .OrderByDescending(rc => rc.ClosedAt!.Value)
            .ToList();
        int consecutiveLosses = 0;
        foreach (var rc in recentClosed)
        {
            if (rc.Status == TradeStatus.CLOSED_LOSS) consecutiveLosses++;
            else break;
        }
        if (consecutiveLosses >= _systemState.MaxConsecutiveLosses)
        {
            var msg = $"Circuit breaker — {consecutiveLosses} LOSS berturut-turut (≥ {_systemState.MaxConsecutiveLosses}). " +
                      $"Resume manual via dashboard.";
            _logger.LogWarning("Trade skipped: {Reason}", msg);
            var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
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

        // Hard $ caps untuk Nano mode (full-auto safety, modal kecil $30-60)
        if (_mode.CurrentMode == TradeMode.Real)
        {
            var nanoTier = RiskTier.FromEquity(currentEquity, _mode.CurrentMode);
            if (nanoTier.Name == "nano")
            {
                // Floor: equity drop ke level kritis → permanent halt sampai user manual review
                if (_systemState.NanoEquityFloorUsd > 0m && currentEquity <= _systemState.NanoEquityFloorUsd)
                {
                    var msg = $"NANO FLOOR HIT: equity ${currentEquity:F2} ≤ floor ${_systemState.NanoEquityFloorUsd:F2}. PERMANENT HALT — review modal sebelum resume.";
                    _logger.LogCritical(msg);
                    _systemState.Halt(msg);
                    var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
                    await _positions.SaveAsync(skipped);
                    return skipped;
                }

                // Daily $ loss cap: hitung total realized PnL hari ini (UTC day)
                if (_systemState.NanoMaxDailyLossUsd > 0m)
                {
                    var todayUtc = DateTimeOffset.UtcNow.Date;
                    var allClosed = (await _positions.GetAllAsync())
                        .Where(p => p.ClosedAt.HasValue &&
                                    p.ClosedAt.Value.UtcDateTime.Date == todayUtc &&
                                    (p.Status == TradeStatus.CLOSED_WIN || p.Status == TradeStatus.CLOSED_LOSS))
                        .ToList();
                    decimal todayRealizedPnl = allClosed.Sum(p => p.FloatingPnl);
                    if (todayRealizedPnl <= -_systemState.NanoMaxDailyLossUsd)
                    {
                        var msg = $"NANO DAILY LOSS CAP: today realized ${todayRealizedPnl:F2} ≤ -${_systemState.NanoMaxDailyLossUsd:F2}. Auto-halt sampai UTC midnight.";
                        _logger.LogWarning(msg);
                        _systemState.Halt(msg);
                        var skipped = TradePosition.CreateSkipped(tradeId, signal.RunId, signal.Pair, msg);
                        await _positions.SaveAsync(skipped);
                        return skipped;
                    }
                }
            }
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

        // Tier-aware: dapatkan tier dulu untuk hitung risk limit + handle Nano override
        var tier = RiskTier.FromEquity(currentEquity, _mode.CurrentMode);
        var p = request.RiskValidation.ValidatedParameters!;

        // Per-trade risk override (slider Nano mode di dashboard)
        // Pakai override kalau di dalam tier limit (1% sampai 10%) dan tier == nano (yang accept higher).
        if (request.RiskPctOverride is decimal overridePct && overridePct > 0m)
        {
            if (tier.Name != "nano")
            {
                _logger.LogInformation("RiskPctOverride {Pct:P1} diabaikan — hanya berlaku di Nano tier (current: {Tier})", overridePct, tier.Name);
            }
            else if (overridePct < 0.01m || overridePct > 0.10m)
            {
                _logger.LogWarning("RiskPctOverride {Pct:P1} di luar range [1%, 10%] — pakai tier default {Default:P1}", overridePct, tier.RiskPerTradePct);
            }
            else
            {
                // Recalculate parameters dengan override risk %.
                decimal newRiskAmount = Math.Round(currentEquity * overridePct, 2);
                int slPips = p.StopLossPips;
                decimal newLotSize = Math.Max(Math.Round(newRiskAmount / (slPips * 10m), 2), 0.01m);
                decimal newPotentialProfit = Math.Round(newLotSize * p.TakeProfitPips * 10m, 2);

                _logger.LogInformation("Nano override: risk {Old:P1} → {New:P1} (lot {OldLot} → {NewLot})",
                    tier.RiskPerTradePct, overridePct, p.LotSize, newLotSize);

                p = p with { RiskAmount = newRiskAmount, LotSize = newLotSize, PotentialProfit = newPotentialProfit };
            }
        }

        // ── Dynamic Sizing: confidence-weighted + post-loss adaptation ───────
        // 1. Confidence multiplier: setup A+ (conf 85%+) dapat lot lebih besar,
        //    setup mediocre (conf 60-65%) dapat lot lebih kecil.
        //    Formula: 0.7 + (conf - 0.6) × 1.5, clamped [0.7, 1.3]
        // 2. Loss-adapt multiplier: setiap loss berturut-turut kurangi 25%.
        //    0 loss → 1.0×, 1 loss → 0.8×, 2 loss → 0.67×, 3+ → halt (sudah ada).
        //
        // Compound: final risk = base × confMult × lossMult.
        // Tidak override Nano slider (kalau user explicit set, hormati pilihan).
        if (request.RiskPctOverride is null)
        {
            decimal confMult = Math.Clamp(0.7m + (signal.ConfidenceScore - 0.6m) * 1.5m, 0.7m, 1.3m);
            decimal lossMult = 1.0m / (1.0m + consecutiveLosses * 0.25m);
            decimal totalMult = confMult * lossMult;

            if (Math.Abs(totalMult - 1.0m) > 0.01m)  // skip kalau effectively 1.0
            {
                decimal newRiskAmount = Math.Round(p.RiskAmount * totalMult, 2);
                int slPips = p.StopLossPips;
                decimal newLotSize = Math.Max(Math.Round(newRiskAmount / (slPips * 10m), 2), 0.01m);
                decimal newPotentialProfit = Math.Round(newLotSize * p.TakeProfitPips * 10m, 2);

                _logger.LogInformation(
                    "Dynamic sizing: confMult={Conf:F2} (conf {ConfPct:P0}), lossMult={Loss:F2} ({L} cons. loss), risk ${OldRisk}→${NewRisk}, lot {OldLot}→{NewLot}",
                    confMult, signal.ConfidenceScore, lossMult, consecutiveLosses,
                    p.RiskAmount, newRiskAmount, p.LotSize, newLotSize);

                p = p with { RiskAmount = newRiskAmount, LotSize = newLotSize, PotentialProfit = newPotentialProfit };
            }
        }

        // Hard limit risk per trade — tier-aware (nano: max 10%, lainnya: tier max + 5bp toleransi)
        decimal maxRiskPct = tier.Name == "nano" ? 0.10m : (tier.RiskPerTradePct + 0.0005m);
        var actualRiskPct = currentEquity > 0 ? p.RiskAmount / currentEquity : p.RiskAmount;
        if (actualRiskPct > maxRiskPct)
        {
            var msg = $"Risk {actualRiskPct:P2} exceeds tier '{tier.Name}' max {maxRiskPct:P2}";
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
                externalTradeId: brokerResult.ExternalId,
                timeframe: signal.Timeframe);

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
                p.RiskRewardRatio,
                timeframe: signal.Timeframe);

            _logger.LogInformation(
                "Trade OPEN [SIMULATION] {TradeId} — {Direction} {Pair} @ {Entry}, SL {SL}, TP {TP}, lot {Lot}",
                tradeId, signal.Signal, signal.Pair,
                p.Entry, p.StopLoss, p.TakeProfit, p.LotSize);
        }

        await _positions.SaveAsync(position);
        return position;
    }
}
