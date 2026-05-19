namespace ForexAI.Application.UseCases.GetAdaptiveStats;

/// <summary>
/// Adaptive learning stats — per-bucket WR + expectancy + Wilson confidence interval +
/// sample size + global gate state. Layer 3 (Learning Engine) baca dari sini untuk
/// trigger evaluation. Dashboard /adaptive route render data ini untuk observation.
/// </summary>
public record AdaptiveStatsResult(
    int     TotalTradeCount,           // semua trade closed dari history (untuk global ≥ 50 gate)
    int     WindowTradeCount,          // berapa trade dalam rolling window
    int     WindowSize,                // window size yang dipakai
    bool    GlobalGateOpen,            // totalTradeCount >= 50 ?
    decimal OverallWinRate,            // 0..1, rolling window
    decimal OverallExpectancyR,        // R-multiple expectancy (avg pnl / avg risk)
    decimal OverallExpectancyUsd,      // USD expectancy
    IReadOnlyList<BucketStat> ByRegime,
    IReadOnlyList<BucketStat> BySession,
    IReadOnlyList<BucketStat> ByPattern,
    IReadOnlyList<BucketStat> ByZone,
    IReadOnlyList<BucketStat> ByConfidenceBand,
    IReadOnlyList<BucketStat> BySweepFlag,       // Sweep vs No-Sweep
    IReadOnlyList<BucketStat> ByExitReason);

/// <summary>
/// Per-bucket statistic dengan Wilson 95% confidence interval.
/// `BucketReady` = bucket punya cukup sample (≥ 20) untuk action consideration.
/// `WilsonLower`/`WilsonUpper` = bounds untuk reliable comparison vs baseline.
/// </summary>
public record BucketStat(
    string  Label,
    int     Trades,
    int     Wins,
    decimal WinRate,             // 0..1
    decimal WilsonLower95,       // lower bound 95% Wilson interval
    decimal WilsonUpper95,       // upper bound 95% Wilson interval
    decimal AvgPnlUsd,
    decimal TotalPnlUsd,
    decimal ExpectancyR,         // R-multiple = avgPnl / avgRisk
    decimal AvgMfePips,          // average max favorable excursion
    decimal AvgMaePips,          // average max adverse excursion (negative)
    bool    BucketReady);        // Trades >= 20
