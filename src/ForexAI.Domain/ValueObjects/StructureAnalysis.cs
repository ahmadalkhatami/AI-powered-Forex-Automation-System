namespace ForexAI.Domain.ValueObjects;

public record StructureAnalysis(
    decimal NearestSupport,
    decimal NearestResistance,
    decimal Score,
    string ScoreRationale,
    bool CandleConfirmed,
    string CandlePattern,
    string PricePosition
);
