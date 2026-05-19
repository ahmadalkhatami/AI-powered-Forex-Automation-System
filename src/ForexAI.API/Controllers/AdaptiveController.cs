using System.Text.Json;
using ForexAI.Application.UseCases.GetAdaptiveStats;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/adaptive")]
public class AdaptiveController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IAdaptiveStateService _adaptiveState;

    public AdaptiveController(IMediator mediator, IAdaptiveStateService adaptiveState)
    {
        _mediator = mediator;
        _adaptiveState = adaptiveState;
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

    /// <summary>Current adaptive state — overrides aktif + audit history.</summary>
    [HttpGet("state")]
    public ActionResult<AdaptiveState> State() => Ok(_adaptiveState.Current);

    /// <summary>List snapshot IDs (newest first, max 50).</summary>
    [HttpGet("snapshots")]
    public ActionResult<IReadOnlyList<string>> ListSnapshots() => Ok(_adaptiveState.ListSnapshots());

    /// <summary>Full snapshot bundle (before/after/reason JSON).</summary>
    [HttpGet("snapshots/{snapshotId}")]
    public ActionResult<AdaptiveSnapshotResponse> GetSnapshot(string snapshotId)
    {
        var bundle = _adaptiveState.ReadSnapshot(snapshotId);
        if (bundle is null) return NotFound();
        return Ok(new AdaptiveSnapshotResponse(
            SnapshotId: bundle.SnapshotId,
            ConfigBefore: JsonDocument.Parse(bundle.ConfigBeforeJson).RootElement,
            ConfigAfter:  JsonDocument.Parse(bundle.ConfigAfterJson).RootElement,
            Reason:       JsonDocument.Parse(bundle.ReasonJson).RootElement));
    }

    /// <summary>
    /// Rollback ke before-state dari snapshot tertentu. Manual approval only — return 200
    /// kalau berhasil restore, 404 kalau snapshot tidak ada, 400 kalau gagal parse.
    /// </summary>
    [HttpPost("rollback/{snapshotId}")]
    public ActionResult<RollbackResponse> Rollback(string snapshotId, [FromBody] RollbackRequest req)
    {
        var ok = _adaptiveState.Rollback(snapshotId, req.RequestedBy);
        if (!ok) return BadRequest(new RollbackResponse(false, "Snapshot not found atau gagal parse"));
        return Ok(new RollbackResponse(true, $"Restored from {snapshotId}"));
    }

    /// <summary>Master kill switch — disable seluruh Adaptive Engine.</summary>
    [HttpPost("disable")]
    public ActionResult SetMasterDisabled([FromBody] ToggleRequest req)
    {
        _adaptiveState.SetMasterDisabled(req.Disabled);
        return Ok();
    }

    /// <summary>Per-action kill switch.</summary>
    [HttpPost("action/{actionName}/disable")]
    public ActionResult SetActionDisabled(string actionName, [FromBody] ToggleRequest req)
    {
        _adaptiveState.SetActionDisabled(actionName, req.Disabled);
        return Ok();
    }
}

public record AdaptiveSnapshotResponse(
    string SnapshotId,
    JsonElement ConfigBefore,
    JsonElement ConfigAfter,
    JsonElement Reason);

public record RollbackRequest(string RequestedBy = "user");
public record RollbackResponse(bool Success, string Message);
public record ToggleRequest(bool Disabled);
