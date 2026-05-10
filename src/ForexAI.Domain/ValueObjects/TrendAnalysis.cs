namespace ForexAI.Domain.ValueObjects;

public record TrendAnalysis(
    string Bias,
    string Strength,
    decimal Score,
    bool HtfAligned,
    string Configuration,
    string ScoreRationale
);
