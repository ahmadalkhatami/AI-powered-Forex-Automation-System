namespace ForexAI.Infrastructure.Persistence.Dtos;

internal record TradePositionDto
{
    public string TradeId { get; init; } = "";
    public string RunId { get; init; } = "";
    public string Status { get; init; } = "";
    public string Pair { get; init; } = "";
    public string Direction { get; init; } = "";
    public decimal Entry { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }
    public decimal LotSize { get; init; }
    public decimal RiskAmount { get; init; }
    public decimal PotentialProfit { get; init; }
    public decimal RiskReward { get; init; }
    public decimal FloatingPnl { get; init; }
    public int FloatingPnlPips { get; init; }
    public DateTimeOffset? OpenedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public string Mode { get; init; } = "";
    public string? SkipReason { get; init; }
    public string? ExternalTradeId { get; init; }
    public string? Timeframe { get; init; }

    // Adaptive Learning enrich (P0). Nullable — backward compat dengan trade existing.
    public string? SessionAtEntry { get; init; }
    public string? RegimeAtEntry { get; init; }
    public string? PatternName { get; init; }
    public string? PatternBias { get; init; }
    public decimal? PatternReliability { get; init; }
    public bool? SweepDetected { get; init; }
    public string? ZoneAtEntry { get; init; }
    public decimal? ConfidenceAtEntry { get; init; }
    public int? MfePips { get; init; }
    public int? MaePips { get; init; }
    public string? ExitReason { get; init; }
}
