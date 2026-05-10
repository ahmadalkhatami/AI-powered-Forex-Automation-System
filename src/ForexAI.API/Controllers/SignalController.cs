using ForexAI.API.Models;
using ForexAI.Application.UseCases.AnalyzeSignal;
using ForexAI.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/signal")]
public class SignalController : ControllerBase
{
    private readonly IMediator _mediator;

    public SignalController(IMediator mediator) => _mediator = mediator;

    [HttpPost("analyze")]
    public async Task<ActionResult<TradeSignal>> Analyze(
        [FromBody] AnalyzeSignalRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AnalyzeSignalCommand(request.Pair, request.Timeframe), ct);
        return Ok(result);
    }
}
