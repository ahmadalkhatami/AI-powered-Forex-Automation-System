using ForexAI.API.Models;
using ForexAI.Application.UseCases.EvaluateRisk;
using ForexAI.Domain.Enums;
using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/risk")]
public class RiskController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly AuditLogger _audit;

    public RiskController(IMediator mediator, AuditLogger audit)
    {
        _mediator = mediator;
        _audit    = audit;
    }

    [HttpPost("evaluate")]
    public async Task<ActionResult<RiskValidation>> Evaluate(
        [FromBody] EvaluateRiskRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<SignalDirection>(request.FinalDecision, ignoreCase: true, out var direction))
            return BadRequest(new { error = $"Invalid finalDecision: '{request.FinalDecision}'. Valid values: BUY, SELL, HOLD" });

        var predictor = new PredictorResult(
            FinalDecision: direction,
            AdjustedConfidence: request.AdjustedConfidence,
            TotalScore: request.TotalScore,
            AgreementScore: request.AgreementScore,
            OverrideSignal: null,
            ValidationNotes: Array.Empty<string>());

        var result = await _mediator.Send(
            new EvaluateRiskCommand(request.SignalId, predictor, request.Equity, request.OpenPositions), ct);

        _audit.Log("risk",
            $"{result.Decision} for signal {request.SignalId} · {request.AdjustedConfidence:P0} confidence",
            new { signalId = request.SignalId, decision = result.Decision,
                  cautionNotes = result.CautionNotes, noGoReasons = result.NoGoReasons });

        return Ok(result);
    }
}
