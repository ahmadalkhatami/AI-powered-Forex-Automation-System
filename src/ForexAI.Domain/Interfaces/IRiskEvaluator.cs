using ForexAI.Domain.Entities;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Domain.Interfaces;

public interface IRiskEvaluator
{
    Task<RiskValidation> EvaluateAsync(
        TradeSignal signal,
        PredictorResult predictor,
        decimal equity,
        int openPositions,
        DailyRiskUsage dailyUsage);
}
