using ForexAI.Domain.ValueObjects;
using MediatR;

namespace ForexAI.Application.UseCases.EvaluateRisk;

public record EvaluateRiskCommand(
    Guid SignalId,
    PredictorResult Predictor,
    decimal Equity,
    int OpenPositions
) : IRequest<RiskValidation>;
