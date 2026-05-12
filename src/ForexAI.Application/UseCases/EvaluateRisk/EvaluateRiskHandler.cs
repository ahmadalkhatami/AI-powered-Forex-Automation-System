using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ForexAI.Application.UseCases.EvaluateRisk;

public class EvaluateRiskHandler : IRequestHandler<EvaluateRiskCommand, RiskValidation>
{
    private readonly ISignalRepository            _signals;
    private readonly ITradePositionRepository     _positions;
    private readonly IRiskEvaluator               _riskEvaluator;
    private readonly ILogger<EvaluateRiskHandler> _logger;

    public EvaluateRiskHandler(
        ISignalRepository            signals,
        ITradePositionRepository     positions,
        IRiskEvaluator               riskEvaluator,
        ILogger<EvaluateRiskHandler> logger)
    {
        _signals       = signals;
        _positions     = positions;
        _riskEvaluator = riskEvaluator;
        _logger        = logger;
    }

    public async Task<RiskValidation> Handle(EvaluateRiskCommand request, CancellationToken cancellationToken)
    {
        var signal = await _signals.GetByIdAsync(request.SignalId)
            ?? throw new InvalidOperationException($"Signal {request.SignalId} not found");

        var dailyUsage = await _positions.GetDailyRiskUsageAsync(DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Evaluating risk for signal {SignalId} — equity: ${Equity}, open positions: {Open}, " +
            "daily risk used: ${UsedUsd:F2} ({Count} trades today)",
            request.SignalId, request.Equity, request.OpenPositions,
            dailyUsage.UsedUsd, dailyUsage.TradeCount);

        var validation = await _riskEvaluator.EvaluateAsync(
            signal, request.Predictor, request.Equity, request.OpenPositions, dailyUsage);

        _logger.LogInformation("Risk decision: {Decision} ({PositionDecision})",
            validation.Decision, validation.PositionDecision);

        return validation;
    }
}
