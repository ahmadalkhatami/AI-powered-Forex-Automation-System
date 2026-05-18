using ForexAI.Application.UseCases.GetAnalytics;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnalyticsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Trade quality analytics — win rate, expectancy, performance breakdown
    /// by confidence band / regime / session / timeframe.
    /// Sumber: closed positions di execution-log mode aktif.
    /// </summary>
    [HttpGet("performance")]
    public async Task<ActionResult<AnalyticsResult>> Performance(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAnalyticsQuery(), ct);
        return Ok(result);
    }
}
