namespace ForexAI.API.Models;

public record EvaluateRiskRequest(
    Guid SignalId,
    string FinalDecision,
    decimal AdjustedConfidence,
    int TotalScore,
    decimal AgreementScore,
    decimal Equity,
    int OpenPositions);
