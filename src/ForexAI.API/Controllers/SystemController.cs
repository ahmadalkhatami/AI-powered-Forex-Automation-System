using ForexAI.API.Hubs;
using ForexAI.Application.UseCases.ClosePosition;
using ForexAI.Application.UseCases.GetAccountHealth;
using ForexAI.Application.UseCases.GetAllPositions;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ForexAI.API.Controllers;

public record HaltRequest(string Reason);

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    private readonly ISystemStateService _systemState;
    private readonly AuditLogger _audit;
    private readonly IMediator _mediator;
    private readonly IBrokerService _broker;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ILogger<SystemController> _logger;

    public SystemController(
        ISystemStateService systemState,
        AuditLogger audit,
        IMediator mediator,
        IBrokerService broker,
        IHubContext<DashboardHub> hub,
        ILogger<SystemController> logger)
    {
        _systemState = systemState;
        _audit       = audit;
        _mediator    = mediator;
        _broker      = broker;
        _hub         = hub;
        _logger      = logger;
    }

    [HttpGet("state")]
    public IActionResult GetState() => Ok(new
    {
        isHalted             = _systemState.IsHalted,
        haltReason           = _systemState.HaltReason,
        haltedAt             = _systemState.HaltedAt,
        maxSpreadPips        = _systemState.MaxSpreadPips,
        maxConsecutiveLosses = _systemState.MaxConsecutiveLosses,
    });

    /// <summary>
    /// Kill switch — halt sistem + (optional) close semua active positions.
    /// Setelah halt: PlaceOrder/ExecuteTrade akan reject sampai /resume dipanggil.
    /// </summary>
    [HttpPost("halt")]
    public async Task<IActionResult> Halt([FromBody] HaltRequest req, [FromQuery] bool closeAll = true)
    {
        var reason = string.IsNullOrWhiteSpace(req.Reason) ? "User-triggered emergency halt" : req.Reason;
        _systemState.Halt(reason);
        _audit.Log("halt", $"System HALTED: {reason}", new { closeAll });

        var closedTickets = new List<string>();
        var failures = new List<string>();

        if (closeAll && _broker.IsLive)
        {
            var openPositions = await _mediator.Send(new GetAllPositionsQuery());
            var active = openPositions.Where(p => p.Status == TradeStatus.ACTIVE).ToList();

            foreach (var pos in active)
            {
                try
                {
                    var result = await _broker.ClosePositionAsync(pos);
                    if (result.IsSuccess)
                    {
                        // Lokal: mark as CLOSED dengan outcome berdasarkan floating PnL
                        var outcome = pos.FloatingPnl >= 0m
                            ? TradeStatus.CLOSED_WIN
                            : TradeStatus.CLOSED_LOSS;
                        await _mediator.Send(new ClosePositionCommand(pos.TradeId, outcome, result.ExecutedPrice));
                        closedTickets.Add(pos.TradeId);
                        _audit.Log("close", $"HALT-close {pos.TradeId} ({pos.Pair})",
                            new { fillPrice = result.ExecutedPrice, floatingPnl = pos.FloatingPnl });
                    }
                    else
                    {
                        failures.Add($"{pos.TradeId}: {result.ErrorMessage ?? "unknown"}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gagal close position {Id} saat halt", pos.TradeId);
                    failures.Add($"{pos.TradeId}: {ex.Message}");
                }
            }
        }

        // Broadcast state ke dashboard
        try
        {
            var account = await _mediator.Send(new GetAccountHealthQuery());
            await _hub.Clients.All.SendAsync("account", account);
            var positions = await _mediator.Send(new GetAllPositionsQuery());
            await _hub.Clients.All.SendAsync("positions", positions);
        }
        catch { /* best effort */ }

        return Ok(new
        {
            halted = true,
            reason,
            closedCount = closedTickets.Count,
            closedTickets,
            failures,
        });
    }

    /// <summary>
    /// Resume dari halt state. Execute path akan kembali normal.
    /// </summary>
    [HttpPost("resume")]
    public async Task<IActionResult> Resume()
    {
        _systemState.Resume();
        _audit.Log("resume", "System resumed (halt cleared)");

        try
        {
            var account = await _mediator.Send(new GetAccountHealthQuery());
            await _hub.Clients.All.SendAsync("account", account);
        }
        catch { /* best effort */ }

        return Ok(new { halted = false });
    }
}
