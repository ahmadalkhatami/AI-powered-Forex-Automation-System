namespace ForexAI.Application.UseCases.GetAnalytics;

public record AnalyticsResult(
    int    TotalTrades,
    int    WinCount,
    int    LossCount,
    decimal WinRate,           // 0..1
    decimal AvgWinUsd,
    decimal AvgLossUsd,
    decimal ExpectancyUsd,     // avgWin*winRate − avgLoss*lossRate
    decimal TotalNetPnlUsd,
    decimal AvgRealizedRR,     // realized PnL / risk amount
    IReadOnlyList<BucketStats> ByConfidenceBand,    // 60-70, 70-80, 80+
    IReadOnlyList<BucketStats> ByRegime,            // Trending, Transitional, Ranging
    IReadOnlyList<BucketStats> BySession,           // London, NY, Tokyo, Sydney, Closed
    IReadOnlyList<BucketStats> ByTimeframe,         // M15, H1, D1
    IReadOnlyList<BucketStats> ByPattern,           // Bullish Pin Bar, Engulfing, None, ...
    IReadOnlyList<BucketStats> BySignalSource       // "MA Cross" vs "Breakout Promoted"
);

/// <summary>Performance bucket — group by attribute, hitung win rate + expectancy.</summary>
public record BucketStats(
    string Label,
    int    Trades,
    int    Wins,
    decimal WinRate,
    decimal AvgPnlUsd,
    decimal TotalPnlUsd);
