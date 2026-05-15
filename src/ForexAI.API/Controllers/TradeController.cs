using ForexAI.API.Models;
using ForexAI.Application.UseCases.ExecuteTrade;
using ForexAI.Domain.Entities;
using ForexAI.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/trade")]
public class TradeController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly AuditLogger _audit;

    public TradeController(IMediator mediator, AuditLogger audit)
    {
        _mediator = mediator;
        _audit    = audit;
    }

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

        _audit.Log("execute",
            $"{result.Status} {result.Pair} {result.Direction} lot={result.LotSize} mode={result.Mode}" +
            (result.SkipReason is null ? "" : $" — {result.SkipReason}"),
            new { tradeId = result.TradeId, signalId = request.SignalId,
                  status = result.Status.ToString(), entry = result.Entry,
                  sl = result.StopLoss, tp = result.TakeProfit,
                  lot = result.LotSize, mode = result.Mode, skipReason = result.SkipReason });

        return Ok(result);
    }
}
