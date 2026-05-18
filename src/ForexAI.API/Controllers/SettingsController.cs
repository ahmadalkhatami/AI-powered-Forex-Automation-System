using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly ISystemStateService _systemState;
    private readonly AuditLogger _audit;

    public SettingsController(ISystemStateService systemState, AuditLogger audit)
    {
        _systemState = systemState;
        _audit       = audit;
    }

    /// <summary>Read current config — semua thresholds di SystemStateService.</summary>
    [HttpGet]
    public ActionResult<SettingsResponse> Get()
    {
        return Ok(new SettingsResponse(
            MaxSpreadPips:        _systemState.MaxSpreadPips,
            MaxConsecutiveLosses: _systemState.MaxConsecutiveLosses,
            MaxHoldingMinutes:    _systemState.MaxHoldingMinutes,
            CooldownMinutes:      _systemState.CooldownMinutes,
            NanoMaxDailyLossUsd:  _systemState.NanoMaxDailyLossUsd,
            NanoEquityFloorUsd:   _systemState.NanoEquityFloorUsd,
            MaxWeeklyDrawdownPct: _systemState.MaxWeeklyDrawdownPct,
            IsHalted:             _systemState.IsHalted,
            HaltReason:           _systemState.HaltReason));
    }

    /// <summary>Update config values. Null/omit field = jangan ubah.</summary>
    [HttpPost]
    public ActionResult<SettingsResponse> Update([FromBody] SettingsUpdateRequest req)
    {
        _systemState.UpdateConfig(
            maxSpreadPips:        req.MaxSpreadPips,
            maxConsecutiveLosses: req.MaxConsecutiveLosses,
            maxHoldingMinutes:    req.MaxHoldingMinutes,
            cooldownMinutes:      req.CooldownMinutes,
            nanoMaxDailyLossUsd:  req.NanoMaxDailyLossUsd,
            nanoEquityFloorUsd:   req.NanoEquityFloorUsd,
            maxWeeklyDrawdownPct: req.MaxWeeklyDrawdownPct);

        _audit.Log("settings", "Config updated", req);
        return Get();
    }
}

public record SettingsResponse(
    decimal MaxSpreadPips,
    int MaxConsecutiveLosses,
    int MaxHoldingMinutes,
    int CooldownMinutes,
    decimal NanoMaxDailyLossUsd,
    decimal NanoEquityFloorUsd,
    decimal MaxWeeklyDrawdownPct,
    bool IsHalted,
    string? HaltReason);

public record SettingsUpdateRequest(
    decimal? MaxSpreadPips = null,
    int? MaxConsecutiveLosses = null,
    int? MaxHoldingMinutes = null,
    int? CooldownMinutes = null,
    decimal? NanoMaxDailyLossUsd = null,
    decimal? NanoEquityFloorUsd = null,
    decimal? MaxWeeklyDrawdownPct = null);
