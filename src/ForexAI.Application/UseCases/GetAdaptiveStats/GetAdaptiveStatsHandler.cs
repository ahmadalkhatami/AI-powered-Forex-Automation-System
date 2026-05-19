using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using MediatR;

namespace ForexAI.Application.UseCases.GetAdaptiveStats;

/// <summary>
/// Compute rolling per-bucket adaptive stats dari TradePosition enriched (post-P0).
/// Pakai field SessionAtEntry / RegimeAtEntry / PatternName / ZoneAtEntry / SweepDetected /
/// ConfidenceAtEntry / MfePips / MaePips / ExitReason yang di-stamp saat trade open + close.
/// </summary>
public class GetAdaptiveStatsHandler : IRequestHandler<GetAdaptiveStatsQuery, AdaptiveStatsResult>
{
    private const int GlobalTradeFloor = 50;
    private const int BucketMinSample  = 20;

    private readonly ITradePositionRepository _positions;

    public GetAdaptiveStatsHandler(ITradePositionRepository positions)
    {
        _positions = positions;
    }

    public async Task<AdaptiveStatsResult> Handle(GetAdaptiveStatsQuery request, CancellationToken cancellationToken)
    {
        var all = await _positions.GetAllAsync();
        var closed = all
            .Where(p => p.Status == TradeStatus.CLOSED_WIN || p.Status == TradeStatus.CLOSED_LOSS)
            .OrderByDescending(p => p.ClosedAt)
            .ToList();

        int totalCount = closed.Count;

        // Rolling window — default 30 trade terakhir. WindowSize 0 = all history.
        var window = request.WindowSize > 0 && closed.Count > request.WindowSize
            ? closed.Take(request.WindowSize).ToList()
            : closed;

        if (window.Count == 0)
        {
            return new AdaptiveStatsResult(
                TotalTradeCount: 0, WindowTradeCount: 0, WindowSize: request.WindowSize,
                GlobalGateOpen: false, OverallWinRate: 0m, OverallExpectancyR: 0m, OverallExpectancyUsd: 0m,
                ByRegime: Array.Empty<BucketStat>(), BySession: Array.Empty<BucketStat>(),
                ByPattern: Array.Empty<BucketStat>(), ByZone: Array.Empty<BucketStat>(),
                ByConfidenceBand: Array.Empty<BucketStat>(), BySweepFlag: Array.Empty<BucketStat>(),
                ByExitReason: Array.Empty<BucketStat>());
        }

        int wins   = window.Count(p => p.Status == TradeStatus.CLOSED_WIN);
        decimal totalPnl = window.Sum(p => p.FloatingPnl);
        decimal totalRisk = window.Where(p => p.RiskAmount > 0m).Sum(p => p.RiskAmount);
        decimal overallWinRate = (decimal)wins / window.Count;
        decimal overallExpectancyR = totalRisk > 0m ? Math.Round(totalPnl / totalRisk, 3) : 0m;
        decimal overallExpectancyUsd = Math.Round(totalPnl / window.Count, 2);

        // Bucket aggregations
        var byRegime  = BucketBy(window, p => p.RegimeAtEntry ?? "Unknown");
        var bySession = BucketBy(window, p => p.SessionAtEntry ?? "Unknown");
        var byPattern = BucketBy(window, p => string.IsNullOrEmpty(p.PatternName) ? "None" : p.PatternName);
        var byZone    = BucketBy(window, p => p.ZoneAtEntry ?? "Unknown");
        var byConfBand = BucketBy(window, p => ConfidenceBandLabel(p.ConfidenceAtEntry));
        var bySweep   = BucketBy(window, p =>
            p.SweepDetected == true ? "Sweep Promoted" :
            p.SweepDetected == false ? "No Sweep" : "Unknown");
        var byExit    = BucketBy(window, p => p.ExitReason ?? "Unknown");

        return new AdaptiveStatsResult(
            TotalTradeCount:      totalCount,
            WindowTradeCount:     window.Count,
            WindowSize:           request.WindowSize,
            GlobalGateOpen:       totalCount >= GlobalTradeFloor,
            OverallWinRate:       Math.Round(overallWinRate, 3),
            OverallExpectancyR:   overallExpectancyR,
            OverallExpectancyUsd: overallExpectancyUsd,
            ByRegime:         byRegime,
            BySession:        bySession,
            ByPattern:        byPattern,
            ByZone:           byZone,
            ByConfidenceBand: byConfBand,
            BySweepFlag:      bySweep,
            ByExitReason:     byExit);
    }

    private static string ConfidenceBandLabel(decimal? conf)
    {
        if (!conf.HasValue) return "Unknown";
        decimal v = conf.Value;
        if (v < 0.60m) return "<60%";
        if (v < 0.70m) return "60-70%";
        if (v < 0.80m) return "70-80%";
        if (v < 0.90m) return "80-90%";
        return "90%+";
    }

    private static IReadOnlyList<BucketStat> BucketBy(
        IReadOnlyList<TradePosition> trades, Func<TradePosition, string> keySelector)
    {
        return trades
            .GroupBy(keySelector)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .OrderByDescending(g => g.Count())
            .Select(g => BuildBucket(g.Key, g.ToList()))
            .ToList();
    }

    private static BucketStat BuildBucket(string label, IReadOnlyList<TradePosition> trades)
    {
        int n = trades.Count;
        int w = trades.Count(p => p.Status == TradeStatus.CLOSED_WIN);
        decimal pnl = trades.Sum(p => p.FloatingPnl);
        decimal totalRisk = trades.Where(p => p.RiskAmount > 0m).Sum(p => p.RiskAmount);

        var (wLow, wHigh) = WilsonInterval.Compute(w, n);

        decimal avgMfe = trades.Any(p => p.MfePips.HasValue)
            ? (decimal)trades.Where(p => p.MfePips.HasValue).Average(p => p.MfePips!.Value)
            : 0m;
        decimal avgMae = trades.Any(p => p.MaePips.HasValue)
            ? (decimal)trades.Where(p => p.MaePips.HasValue).Average(p => p.MaePips!.Value)
            : 0m;

        return new BucketStat(
            Label:         label,
            Trades:        n,
            Wins:          w,
            WinRate:       Math.Round((decimal)w / n, 3),
            WilsonLower95: wLow,
            WilsonUpper95: wHigh,
            AvgPnlUsd:     Math.Round(pnl / n, 2),
            TotalPnlUsd:   Math.Round(pnl, 2),
            ExpectancyR:   totalRisk > 0m ? Math.Round(pnl / totalRisk, 3) : 0m,
            AvgMfePips:    Math.Round(avgMfe, 1),
            AvgMaePips:    Math.Round(avgMae, 1),
            BucketReady:   n >= BucketMinSample);
    }
}
