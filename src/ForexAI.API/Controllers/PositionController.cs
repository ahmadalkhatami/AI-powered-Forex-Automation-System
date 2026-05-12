using ForexAI.API.Models;
using ForexAI.Application.UseCases.ClosePosition;
using ForexAI.Application.UseCases.GetAllPositions;
using ForexAI.Application.UseCases.GetPositionStatus;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/position")]
public class PositionController : ControllerBase
{
    private readonly IMediator _mediator;

    public PositionController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TradePosition>>> GetAll(CancellationToken ct)
    {
        var positions = await _mediator.Send(new GetAllPositionsQuery(), ct);
        return Ok(positions);
    }

    [HttpGet("{pair}")]
    public async Task<ActionResult<TradePosition>> GetStatus(string pair, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPositionStatusQuery(pair), ct);
        return result is null ? NoContent() : Ok(result);
    }

    [HttpPost("{tradeId}/close")]
    public async Task<ActionResult<TradePosition>> Close(
        string tradeId,
        [FromBody] ClosePositionRequest request,
        CancellationToken ct)
    {
        var outcome = request.Outcome.ToUpperInvariant() == "WIN"
            ? TradeStatus.CLOSED_WIN
            : TradeStatus.CLOSED_LOSS;

        var result = await _mediator.Send(
            new ClosePositionCommand(tradeId, outcome, request.ExitPrice), ct);
        return Ok(result);
    }
}
