using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure.Mifx;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

// ── Request / Response DTOs ────────────────────────────────────────

public record MifxTickRequest(
    string Pair,
    decimal Bid,
    decimal Ask,
    long Time,                        // Unix timestamp detik
    decimal? Balance    = null,       // AccountInfoDouble(ACCOUNT_BALANCE)
    decimal? Equity     = null,       // AccountInfoDouble(ACCOUNT_EQUITY)
    // ── Indikator teknikal dari EA v1.15+ ─────────────────────────────
    decimal? Ma20m15    = null,
    decimal? Ma50m15    = null,
    decimal? Ma20h1     = null,
    decimal? Ma50h1     = null,
    decimal? Rsi14      = null,
    int?     RsiDir     = null,       // 1=rising, 0=falling/flat
    decimal? Support    = null,
    decimal? Resistance = null
);

public record MifxStatusRequest(
    string Status,     // "CONNECTED" | "DISCONNECTED"
    string Pair
);

public record MifxOrderResultRequest(
    string CommandId,
    string Status,     // "FILLED" | "FAILED" | "TIMEOUT"
    string? OrderId,
    decimal Price,
    int Retcode
);

// ── Controller ─────────────────────────────────────────────────────

[ApiController]
[Route("api/mifx")]
public class MifxBridgeController : ControllerBase
{
    private readonly MifxPriceFeed    _feed;
    private readonly MifxCommandQueue _queue;
    private readonly ILogger<MifxBridgeController> _logger;

    public MifxBridgeController(
        MifxPriceFeed feed,
        MifxCommandQueue queue,
        ILogger<MifxBridgeController> logger)
    {
        _feed   = feed;
        _queue  = queue;
        _logger = logger;
    }

    // ── Dipanggil EA setiap detik: kirim bid/ask terbaru ──────────

    [HttpPost("tick")]
    public IActionResult ReceiveTick([FromBody] MifxTickRequest req)
    {
        var tick = new MifxTick(
            Pair:           req.Pair,
            Bid:            req.Bid,
            Ask:            req.Ask,
            Time:           DateTimeOffset.FromUnixTimeSeconds(req.Time),
            AccountBalance: req.Balance,
            AccountEquity:  req.Equity,
            MA20_M15:       req.Ma20m15,
            MA50_M15:       req.Ma50m15,
            MA20_H1:        req.Ma20h1,
            MA50_H1:        req.Ma50h1,
            RSI14:          req.Rsi14,
            RSIDir:         req.RsiDir,
            Support:        req.Support,
            Resistance:     req.Resistance);

        _feed.Update(tick);
        return Ok();
    }

    // ── Dipanggil EA setiap detik: ambil perintah order (jika ada) ─

    [HttpGet("command")]
    public IActionResult GetCommand()
    {
        var cmd = _queue.Dequeue();
        if (cmd is null) return NoContent();   // 204 = tidak ada perintah

        _logger.LogInformation(
            "Mengirim perintah ke EA: {Dir} lot={Lot} SL={SL} TP={TP} id={Id}",
            cmd.Direction, cmd.Lots, cmd.StopLoss, cmd.TakeProfit, cmd.CommandId);

        return Ok(cmd);
    }

    // ── Dipanggil EA setelah order dieksekusi ─────────────────────

    [HttpPost("order-result")]
    public IActionResult ReceiveOrderResult([FromBody] MifxOrderResultRequest req)
    {
        _logger.LogInformation(
            "Hasil order dari EA: commandId={Id} status={Status} orderId={OId} price={Price}",
            req.CommandId, req.Status, req.OrderId, req.Price);

        _queue.Complete(new MifxOrderResult(
            req.CommandId,
            req.Status,
            req.OrderId,
            req.Price,
            req.Retcode));

        return Ok();
    }

    // ── Dipanggil EA saat connect/disconnect ─────────────────────

    [HttpPost("status")]
    public IActionResult ReceiveStatus([FromBody] MifxStatusRequest req)
    {
        _logger.LogInformation("MT5 EA status: {Status} | pair: {Pair}", req.Status, req.Pair);
        return Ok();
    }

    // ── Dipanggil Frontend untuk tampilkan live price ─────────────

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var tick = _feed.Latest;
        return Ok(new
        {
            connected      = _feed.IsConnected,
            pair           = tick?.Pair,
            bid            = tick?.Bid,
            ask            = tick?.Ask,
            mid            = tick?.Mid,
            spreadPips     = tick?.Spread,
            time           = tick?.Time,
            accountBalance = tick?.AccountBalance,
            accountEquity  = tick?.AccountEquity,
        });
    }
}
