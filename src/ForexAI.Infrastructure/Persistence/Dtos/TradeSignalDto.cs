namespace ForexAI.Infrastructure.Persistence.Dtos;

internal record TradeSignalDto
{
    public Guid Id { get; init; }
    public string RunId { get; init; } = "";
    public string Pair { get; init; } = "";
    public string Timeframe { get; init; } = "";
    public string Signal { get; init; } = "";
    public int ConfluenceScore { get; init; }
    public decimal ConfidenceScore { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public IList<string> Warnings { get; init; } = new List<string>();

    // MarketSnapshot fields
    public decimal SnapshotCurrentPrice { get; init; }
    public decimal SnapshotMA20_M15 { get; init; }
    public decimal SnapshotMA50_M15 { get; init; }
    public decimal SnapshotMA20_H1 { get; init; }
    public decimal SnapshotMA50_H1 { get; init; }
    public decimal SnapshotMA20_D1 { get; init; }
    public decimal SnapshotMA50_D1 { get; init; }
    public decimal SnapshotRSI14 { get; init; }
    public string SnapshotRSIDirection { get; init; } = "";
    public string SnapshotSupportZone { get; init; } = "";
    public string SnapshotResistanceZone { get; init; } = "";
    public string SnapshotSession { get; init; } = "";
    public DateTimeOffset SnapshotCapturedAt { get; init; }
    public decimal SnapshotATR14 { get; init; }
    public decimal SnapshotADX14 { get; init; }
    public string SnapshotRegime { get; init; } = "";

    // TrendAnalysis fields
    public string TrendBias { get; init; } = "";
    public string TrendStrength { get; init; } = "";
    public decimal TrendScore { get; init; }
    public bool TrendHtfAligned { get; init; }
    public string TrendConfiguration { get; init; } = "";
    public string TrendScoreRationale { get; init; } = "";

    // MomentumAnalysis fields
    public decimal MomentumRSIValue { get; init; }
    public string MomentumRSIDirection { get; init; } = "";
    public string MomentumZone { get; init; } = "";
    public decimal MomentumScore { get; init; }
    public string MomentumScoreRationale { get; init; } = "";
    public string? MomentumDivergence { get; init; }

    // StructureAnalysis fields
    public decimal StructureNearestSupport { get; init; }
    public decimal StructureNearestResistance { get; init; }
    public decimal StructureScore { get; init; }
    public string StructureScoreRationale { get; init; } = "";
    public bool StructureCandleConfirmed { get; init; }
    public string StructureCandlePattern { get; init; } = "";
    public string StructurePricePosition { get; init; } = "";

    // TradeParameters fields
    public decimal ParamsEntry { get; init; }
    public decimal ParamsStopLoss { get; init; }
    public int ParamsStopLossPips { get; init; }
    public decimal ParamsTakeProfit { get; init; }
    public int ParamsTakeProfitPips { get; init; }
    public decimal ParamsLotSize { get; init; }
    public decimal ParamsRiskAmount { get; init; }
    public decimal ParamsPotentialProfit { get; init; }
    public decimal ParamsRiskRewardRatio { get; init; }
}
