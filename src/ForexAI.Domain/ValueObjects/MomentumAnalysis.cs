namespace ForexAI.Domain.ValueObjects;

public record MomentumAnalysis(
    decimal RSIValue,
    string RSIDirection,
    string Zone,
    decimal Score,
    string ScoreRationale,
    string? Divergence
);
