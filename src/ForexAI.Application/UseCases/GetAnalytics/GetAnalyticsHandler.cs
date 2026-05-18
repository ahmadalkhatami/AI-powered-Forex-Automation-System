using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using MediatR;

namespace ForexAI.Application.UseCases.GetAnalytics;

public class GetAnalyticsHandler : IRequestHandler<GetAnalyticsQuery, AnalyticsResult>
{
    private readonly ITradePositionRepository _positions;
    private readonly ISignalRepository _signals;

    public GetAnalyticsHandler(ITradePositionRepository positions, ISignalRepository signals)
    {
        _positions = positions;
        _signals   = signals;
    }

    public async Task<AnalyticsResult> Handle(GetAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var allPositions = await _positions.GetAllAsync();
        var closed = allPositions.Where(p =>
            p.Status == TradeStatus.CLOSED_WIN || p.Status == TradeStatus.CLOSED_LOSS).ToList();

        if (closed.Count == 0)
        {
            return new AnalyticsResult(0, 0, 0, 0m, 0m, 0m, 0m, 0m, 0m,
                Array.Empty<BucketStats>(), Array.Empty<BucketStats>(),
                Array.Empty<BucketStats>(), Array.Empty<BucketStats>());
        }

        var wins   = closed.Where(p => p.Status == TradeStatus.CLOSED_WIN).ToList();
        var losses = closed.Where(p => p.Status == TradeStatus.CLOSED_LOSS).ToList();

        decimal totalPnl = closed.Sum(p => p.FloatingPnl);
        decimal avgWin   = wins.Count > 0   ? Math.Round(wins.Sum(p => p.FloatingPnl) / wins.Count, 2) : 0m;
        decimal avgLoss  = losses.Count > 0 ? Math.Round(Math.Abs(losses.Sum(p => p.FloatingPnl) / losses.Count), 2) : 0m;
        decimal winRate  = (decimal)wins.Count / closed.Count;
        decimal lossRate = 1m - winRate;
        decimal expectancy = Math.Round(avgWin * winRate - avgLoss * lossRate, 2);

        // Realized RR — actual pnl vs risk amount (per trade), averaged
        var rrSamples = closed
            .Where(p => p.RiskAmount > 0m)
            .Select(p => p.FloatingPnl / p.RiskAmount)
            .ToList();
        decimal avgRR = rrSamples.Count > 0 ? Math.Round(rrSamples.Average(), 2) : 0m;

        // ── Bucket: by signal confidence band ─────────────────────────────────
        // Untuk join confidence per trade, fetch signal-history dan match per runId/signalId.
        // Multiple signals bisa share RunId (multiple analyses per run); pakai latest per RunId.
        var allSignals = await _signals.GetAllAsync();
        var signalByRunId = allSignals
            .GroupBy(s => s.RunId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Timestamp).First());

        var withConfidence = closed
            .Select(p => (Position: p, Signal: signalByRunId.GetValueOrDefault(p.RunId)))
            .Where(x => x.Signal != null)
            .ToList();

        var confBuckets = new List<BucketStats>();
        AddConfBucket(confBuckets, "60-70%", withConfidence.Where(x => x.Signal!.ConfidenceScore >= 0.60m && x.Signal.ConfidenceScore < 0.70m).Select(x => x.Position));
        AddConfBucket(confBuckets, "70-80%", withConfidence.Where(x => x.Signal!.ConfidenceScore >= 0.70m && x.Signal.ConfidenceScore < 0.80m).Select(x => x.Position));
        AddConfBucket(confBuckets, "80%+",   withConfidence.Where(x => x.Signal!.ConfidenceScore >= 0.80m).Select(x => x.Position));

        // ── Bucket: by regime ────────────────────────────────────────────────
        var regimeBuckets = new List<BucketStats>();
        foreach (var regime in new[] { "Trending", "Transitional", "Ranging", "Volatile" })
        {
            var bucket = withConfidence.Where(x => x.Signal!.Snapshot.Regime == regime).Select(x => x.Position).ToList();
            if (bucket.Count > 0) AddConfBucket(regimeBuckets, regime, bucket);
        }

        // ── Bucket: by session ───────────────────────────────────────────────
        var sessionBuckets = new List<BucketStats>();
        foreach (var session in new[] { "London", "New York", "London/New York", "Tokyo", "Sydney", "Closed" })
        {
            var bucket = withConfidence.Where(x => x.Signal!.Snapshot.Session == session).Select(x => x.Position).ToList();
            if (bucket.Count > 0) AddConfBucket(sessionBuckets, session, bucket);
        }

        // ── Bucket: by timeframe ─────────────────────────────────────────────
        var tfBuckets = new List<BucketStats>();
        foreach (var tf in new[] { "M15", "H1", "D1" })
        {
            var bucket = closed.Where(p => p.Timeframe == tf).ToList();
            if (bucket.Count > 0) AddConfBucket(tfBuckets, tf, bucket);
        }

        return new AnalyticsResult(
            TotalTrades:   closed.Count,
            WinCount:      wins.Count,
            LossCount:     losses.Count,
            WinRate:       Math.Round(winRate, 3),
            AvgWinUsd:     avgWin,
            AvgLossUsd:    avgLoss,
            ExpectancyUsd: expectancy,
            TotalNetPnlUsd: Math.Round(totalPnl, 2),
            AvgRealizedRR: avgRR,
            ByConfidenceBand: confBuckets,
            ByRegime:         regimeBuckets,
            BySession:        sessionBuckets,
            ByTimeframe:      tfBuckets);
    }

    private static void AddConfBucket(List<BucketStats> sink, string label, IEnumerable<TradePosition> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        var bucketWins = list.Count(p => p.Status == TradeStatus.CLOSED_WIN);
        decimal pnl = list.Sum(p => p.FloatingPnl);
        sink.Add(new BucketStats(
            Label:      label,
            Trades:     list.Count,
            Wins:       bucketWins,
            WinRate:    Math.Round((decimal)bucketWins / list.Count, 3),
            AvgPnlUsd:  Math.Round(pnl / list.Count, 2),
            TotalPnlUsd: Math.Round(pnl, 2)));
    }
}
