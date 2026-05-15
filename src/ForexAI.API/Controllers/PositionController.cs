using ForexAI.API.Models;
using ForexAI.Application.UseCases.ClosePosition;
using ForexAI.Application.UseCases.GetAllPositions;
using ForexAI.Application.UseCases.GetPositionStatus;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure;
using ForexAI.Infrastructure.Mifx;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/position")]
public class PositionController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly AuditLogger _audit;
    private readonly MifxPriceFeed _priceFeed;
    private readonly ITradePositionRepository _positionRepo;
    private readonly ILogger<PositionController> _logger;

    public PositionController(
        IMediator mediator,
        AuditLogger audit,
        MifxPriceFeed priceFeed,
        ITradePositionRepository positionRepo,
        ILogger<PositionController> logger)
    {
        _mediator     = mediator;
        _audit        = audit;
        _priceFeed    = priceFeed;
        _positionRepo = positionRepo;
        _logger       = logger;
    }

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

        _audit.Log("close",
            $"{result.Status} {result.Pair} · pnl ${result.FloatingPnl:F2} ({result.FloatingPnlPips}p) @ {request.ExitPrice}",
            new { tradeId = result.TradeId, outcome = result.Status.ToString(),
                  exitPrice = request.ExitPrice, pnl = result.FloatingPnl, pips = result.FloatingPnlPips });

        return Ok(result);
    }

    // One-click market close: backend tentukan outcome (WIN/LOSS) + exit price otomatis.
    //   - Outcome dari floating P&L terakhir (yang sudah di-sync broker)
    //   - Exit price awal dari MIFX mid; ClosePositionHandler akan override dengan actual
    //     ExecutedPrice dari broker fill kalau live mode + MIFX position
    [HttpPost("{tradeId}/close-market")]
    public async Task<ActionResult<TradePosition>> CloseAtMarket(string tradeId, CancellationToken ct)
    {
        var all = await _positionRepo.GetAllAsync();
        var position = all.FirstOrDefault(p =>
            string.Equals(p.TradeId, tradeId, StringComparison.OrdinalIgnoreCase));
        if (position is null)
            return NotFound(new { error = $"Position {tradeId} tidak ditemukan" });
        if (position.Status != TradeStatus.ACTIVE)
            return BadRequest(new { error = $"Position status {position.Status}, tidak bisa di-close" });

        var tick = _priceFeed.Latest;
        if (tick is null)
            return BadRequest(new { error = "Tidak ada harga MIFX terbaru — EA belum konek?" });

        decimal exitPrice = tick.Mid;
        var outcome = position.FloatingPnl >= 0m ? TradeStatus.CLOSED_WIN : TradeStatus.CLOSED_LOSS;

        try
        {
            var result = await _mediator.Send(new ClosePositionCommand(tradeId, outcome, exitPrice), ct);
            _audit.Log("close",
                $"{result.Status} {result.Pair} · market close · pnl ${result.FloatingPnl:F2} ({result.FloatingPnlPips}p) @ {result.ClosedAt:HH:mm:ss}",
                new { tradeId = result.TradeId, outcome = result.Status.ToString(),
                      exitPrice, pnl = result.FloatingPnl, pips = result.FloatingPnlPips, mode = "market" });
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Market close gagal untuk {Id}: {Err}", tradeId, ex.Message);
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
