using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using MediatR;

namespace ForexAI.Application.UseCases.GetAccountHealth;

public class GetAccountHealthHandler : IRequestHandler<GetAccountHealthQuery, AccountHealthResult>
{
    private const decimal FallbackInitialEquity = 1_000m;
    private const int MaxPositions = 3;

    private readonly ITradePositionRepository _positions;
    private readonly IBrokerService           _broker;
    private readonly ISystemStateService      _systemState;
    private readonly IModeService             _mode;

    public GetAccountHealthHandler(
        ITradePositionRepository positions,
        IBrokerService broker,
        ISystemStateService systemState,
        IModeService mode)
    {
        _positions   = positions;
        _broker      = broker;
        _systemState = systemState;
        _mode        = mode;
    }

    public async Task<AccountHealthResult> Handle(GetAccountHealthQuery _, CancellationToken ct)
    {
        var all = await _positions.GetAllAsync();

        var closed = all.Where(p =>
            p.Status == TradeStatus.CLOSED_WIN ||
            p.Status == TradeStatus.CLOSED_LOSS).ToList();

        var openPositions = all.Count(p => p.Status == TradeStatus.ACTIVE);
        var winCount      = closed.Count(p => p.Status == TradeStatus.CLOSED_WIN);
        var winRate       = closed.Count > 0 ? (decimal)winCount / closed.Count : 0m;

        decimal equity, peakEquity, realizedEquity, unrealizedPnl;
        string  source;

        if (_broker.IsLive)
        {
            // ── Mode Live: ambil data nyata dari akun Monex Demo via MT5 EA ──
            var account = await _broker.GetAccountAsync();

            if (account.Balance > 0 || account.Equity > 0)
            {
                // Data valid dari broker
                equity         = account.Equity  > 0 ? account.Equity  : account.Balance;
                realizedEquity = account.Balance > 0 ? account.Balance : equity;
                unrealizedPnl  = account.UnrealizedPnl;
                peakEquity     = Math.Max(realizedEquity, equity);
                source         = "LIVE";
            }
            else
            {
                // MT5 konek tapi belum terauth ke broker (balance=0 = offline mode)
                var realizedPnl = closed.Sum(p => p.FloatingPnl);
                realizedEquity  = FallbackInitialEquity + realizedPnl;
                unrealizedPnl   = all.Where(p => p.Status == TradeStatus.ACTIVE).Sum(p => p.FloatingPnl);
                equity          = realizedEquity + unrealizedPnl;
                peakEquity      = Math.Max(FallbackInitialEquity, realizedEquity);
                source          = "SIMULATION";   // MT5 online tapi belum login ke broker
            }
        }
        else
        {
            // ── Mode Simulasi: hitung dari trade lokal ──
            var realizedPnl = closed.Sum(p => p.FloatingPnl);
            realizedEquity  = FallbackInitialEquity + realizedPnl;
            unrealizedPnl   = all.Where(p => p.Status == TradeStatus.ACTIVE).Sum(p => p.FloatingPnl);
            equity          = realizedEquity + unrealizedPnl;
            peakEquity      = Math.Max(FallbackInitialEquity, realizedEquity);
            source          = "SIMULATION";
        }

        var drawdownPct = peakEquity > 0
            ? Math.Max(0m, (peakEquity - equity) / peakEquity)
            : 0m;

        // ── Consecutive losses (circuit breaker) ───────────────────────────
        // Iterate closed positions dari yang paling baru; hitung LOSS terus-menerus sampai ketemu WIN.
        var closedNewestFirst = closed
            .Where(p => p.ClosedAt.HasValue)
            .OrderByDescending(p => p.ClosedAt!.Value)
            .ToList();
        int consecutiveLosses = 0;
        foreach (var p in closedNewestFirst)
        {
            if (p.Status == TradeStatus.CLOSED_LOSS) consecutiveLosses++;
            else break;
        }

        // ── Tier-based risk + daily cap snapshot (mode-aware) ────────────────
        var tier        = RiskTier.FromEquity(equity, _mode.CurrentMode);
        var dailyUsage  = await _positions.GetDailyRiskUsageAsync(DateTimeOffset.UtcNow);
        var dailyCapUsd = equity * tier.DailyCapPct;
        var utilization = dailyCapUsd > 0m ? dailyUsage.UsedUsd / dailyCapUsd : 0m;

        return new AccountHealthResult(
            Equity:        Math.Round(equity, 2),
            PeakEquity:    Math.Round(peakEquity, 2),
            RealizedEquity:Math.Round(realizedEquity, 2),
            UnrealizedPnl: Math.Round(unrealizedPnl, 2),
            DrawdownPct:   Math.Round(drawdownPct, 4),
            OpenPositions: openPositions,
            MaxPositions:  MaxPositions,
            TotalTrades:   closed.Count,
            WinRate:       Math.Round(winRate, 4),
            Source:        source,

            RiskTier:            tier.Name,
            RiskPerTradePct:     tier.RiskPerTradePct,
            DailyCapPct:         tier.DailyCapPct,
            MaxDailyTrades:      tier.MaxDailyTrades,
            DailyRiskUsedUsd:    Math.Round(dailyUsage.UsedUsd, 2),
            TradesOpenedToday:   dailyUsage.TradeCount,
            DailyCapUtilization: Math.Round(utilization, 4),

            ConsecutiveLosses:    consecutiveLosses,
            MaxConsecutiveLosses: _systemState.MaxConsecutiveLosses,
            IsHalted:             _systemState.IsHalted,
            HaltReason:           _systemState.HaltReason,
            MaxSpreadPips:        _systemState.MaxSpreadPips,

            Mode:             _mode.CurrentMode.ToString().ToUpperInvariant(),
            IsNanoMode:       tier.Name == "nano",
            EffectiveRiskPct: tier.RiskPerTradePct,

            NanoMaxDailyLossUsd: _systemState.NanoMaxDailyLossUsd,
            NanoEquityFloorUsd:  _systemState.NanoEquityFloorUsd,
            TodayRealizedPnlUsd: Math.Round(closed
                .Where(p => p.ClosedAt.HasValue && p.ClosedAt.Value.UtcDateTime.Date == DateTimeOffset.UtcNow.Date)
                .Sum(p => p.FloatingPnl), 2)
        );
    }
}
