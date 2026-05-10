using ForexAI.API.Models;
using ForexAI.Application.UseCases.EvaluateRisk;
using ForexAI.Domain.Enums;
using ForexAI.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/risk")]
public class RiskController : ControllerBase
{
    private readonly IMediator _mediator;

    public RiskController(IMediator mediator) => _mediator = mediator;

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
        return Ok(result);
    }
}
