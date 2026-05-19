using ForexAI.Application.UseCases.GetAdaptiveStats;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/adaptive")]
public class AdaptiveController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdaptiveController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Per-bucket adaptive learning stats — WR + expectancy + Wilson CI per
    /// regime/session/pattern/zone/confidence-band/sweep/exit-reason.
    ///
    /// <para>Phase 1 = observe only. Dashboard `/adaptive` route render data ini
    /// untuk user review sebelum P2 (auto-action) aktif.</para>
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<AdaptiveStatsResult>> Stats(
        [FromQuery] int window = 30,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetAdaptiveStatsQuery(window), ct);
        return Ok(result);
    }
}
