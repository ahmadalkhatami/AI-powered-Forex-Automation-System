using ForexAI.API.Models;
using ForexAI.Application.UseCases.AnalyzeSignal;
using ForexAI.Domain.Entities;
using ForexAI.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/signal")]
public class SignalController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly AuditLogger _audit;

    public SignalController(IMediator mediator, AuditLogger audit)
    {
        _mediator = mediator;
        _audit    = audit;
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<TradeSignal>> Analyze(
        [FromBody] AnalyzeSignalRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AnalyzeSignalCommand(request.Pair, request.Timeframe), ct);

        _audit.Log("signal",
            $"{result.Signal} {result.Pair} · confidence {result.ConfidenceScore:P0} · regime {result.Snapshot.Regime}",
            new { id = result.Id, signal = result.Signal.ToString(),
                  confidence = result.ConfidenceScore, confluence = result.ConfluenceScore,
                  regime = result.Snapshot.Regime, adx = result.Snapshot.ADX14,
                  parameters = result.Parameters });
        return Ok(result);
    }
}
