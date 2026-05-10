using ForexAI.Domain.Enums;

namespace ForexAI.Domain.ValueObjects;

public record PredictorResult(
    SignalDirection FinalDecision,
    decimal AdjustedConfidence,
    int TotalScore,
    decimal AgreementScore,
    SignalDirection? OverrideSignal,
    IReadOnlyList<string> ValidationNotes
);
