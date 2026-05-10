using ForexAI.Application.UseCases.GetPositionStatus;
using ForexAI.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/position")]
public class PositionController : ControllerBase
{
    private readonly IMediator _mediator;

    public PositionController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{pair}")]
    public async Task<ActionResult<TradePosition>> GetStatus(string pair, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPositionStatusQuery(pair), ct);
        return result is null ? NoContent() : Ok(result);
    }
}
