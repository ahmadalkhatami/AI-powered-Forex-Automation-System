using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

public class RuleBasedRiskEvaluator : IRiskEvaluator
{
    private const decimal MinConfidence = 0.60m;
    private const decimal HighConfidence = 0.70m;
    private const int MaxOpenPositions = 3;

    public Task<RiskValidation> EvaluateAsync(
        TradeSignal signal,
        PredictorResult predictor,
        decimal equity,
        int openPositions)
    {
        var noGoReasons = new List<string>();

        if (signal.Signal == SignalDirection.HOLD)
            noGoReasons.Add("Signal is HOLD — no actionable direction");

        if (predictor.AdjustedConfidence < MinConfidence)
            noGoReasons.Add(
                $"AI confidence {predictor.AdjustedConfidence:P0} below {MinConfidence:P0} minimum");

        if (openPositions >= MaxOpenPositions)
            noGoReasons.Add($"Max open positions reached ({openPositions}/{MaxOpenPositions})");

        if (noGoReasons.Count > 0)
            return Task.FromResult(new RiskValidation(
                "NO-GO", PositionDecision.REJECT, null,
                Array.Empty<string>(), noGoReasons));

        var cautionNotes = new List<string>();
        if (predictor.AdjustedConfidence < HighConfidence)
            cautionNotes.Add(
                $"Confidence {predictor.AdjustedConfidence:P0} is valid but below {HighConfidence:P0} — trade with caution");

        var decision = cautionNotes.Count > 0 ? "GO_WITH_CAUTION" : "GO";

        return Task.FromResult(new RiskValidation(
            decision, PositionDecision.OPEN,
            signal.Parameters, cautionNotes, Array.Empty<string>()));
    }
}
