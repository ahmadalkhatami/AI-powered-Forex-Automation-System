using ForexAI.API.Models;
using ForexAI.Application.UseCases.ExecuteTrade;
using ForexAI.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/trade")]
public class TradeController : ControllerBase
{
    private readonly IMediator _mediator;

    public TradeController(IMediator mediator) => _mediator = mediator;

    [HttpPost("execute")]
    public async Task<ActionResult<TradePosition>> Execute(
        [FromBody] ExecuteTradeRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ExecuteTradeCommand(
                request.SignalId,
                request.RiskValidation,
                request.PeakEquity,
                request.CurrentEquity,
                request.Mode), ct);
        return Ok(result);
    }
}
