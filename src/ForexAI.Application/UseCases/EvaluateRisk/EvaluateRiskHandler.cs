using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ForexAI.Application.UseCases.EvaluateRisk;

public class EvaluateRiskHandler : IRequestHandler<EvaluateRiskCommand, RiskValidation>
{
    private readonly ISignalRepository _signals;
    private readonly IRiskEvaluator _riskEvaluator;
    private readonly ILogger<EvaluateRiskHandler> _logger;

    public EvaluateRiskHandler(
        ISignalRepository signals,
        IRiskEvaluator riskEvaluator,
        ILogger<EvaluateRiskHandler> logger)
    {
        _signals = signals;
        _riskEvaluator = riskEvaluator;
        _logger = logger;
    }

    public async Task<RiskValidation> Handle(EvaluateRiskCommand request, CancellationToken cancellationToken)
    {
        var signal = await _signals.GetByIdAsync(request.SignalId)
            ?? throw new InvalidOperationException($"Signal {request.SignalId} not found");

        _logger.LogInformation("Evaluating risk for signal {SignalId} — equity: ${Equity}, open positions: {Open}",
            request.SignalId, request.Equity, request.OpenPositions);

        var validation = await _riskEvaluator.EvaluateAsync(
            signal, request.Predictor, request.Equity, request.OpenPositions);

        _logger.LogInformation("Risk decision: {Decision} ({PositionDecision})",
            validation.Decision, validation.PositionDecision);

        return validation;
    }
}
