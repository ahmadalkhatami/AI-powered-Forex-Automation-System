using ForexAI.API.Hubs;
using ForexAI.Application.UseCases.GetAccountHealth;
using ForexAI.Application.UseCases.GetAllPositions;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure.Mifx;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ForexAI.API.Controllers;

// ── Request / Response DTOs ────────────────────────────────────────

/// <summary>Satu posisi open dari EA MT5 (EA v1.18+)</summary>
public record MifxPositionDto(
    long    Ticket,
    string  Type,       // "BUY" | "SELL"
    string  Symbol,
    decimal Lots,
    decimal OpenPrice,
    decimal Profit,
    int     Pips
);

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
    decimal? Atr14      = null,       // ATR(14) M15 dalam satuan harga (EA v1.16+)
    decimal? Adx14      = null,       // ADX(14) M15 trend strength 0-100 (EA v1.17+)
    decimal? Support    = null,
    decimal? Resistance = null,
    // ── Posisi open EA (EA v1.18+) ────────────────────────────────────
    List<MifxPositionDto>? Positions  = null
);

public record MifxStatusRequest(
    string Status,     // "CONNECTED" | "DISCONNECTED"
    string Pair
);

public record MifxOrderResultRequest(
    string CommandId,
    string Status,     // "FILLED" | "CLOSED" | "FAILED" | "TIMEOUT"
    string? OrderId,
    decimal Price,
    int Retcode
);

/// <summary>Satu candle bar dari EA (OHLCV)</summary>
public record MifxCandleDto(
    long    Time,    // Unix timestamp detik
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long?   Volume = null
);

/// <summary>Actual realized close profit dari EA history (DEAL_ENTRY_OUT)</summary>
public record MifxClosedPositionRequest(
    long    Ticket,         // MT5 position id
    decimal NetProfit,      // Gross + commission + swap (net realized)
    decimal GrossProfit,
    decimal Commission,
    decimal Swap,
    decimal ClosePrice,     // Actual fill price dari deal history
    long    CloseTime       // Unix timestamp detik
);

// ── Controller ─────────────────────────────────────────────────────

[ApiController]
[Route("api/mifx")]
public class MifxBridgeController : ControllerBase
{
    private readonly MifxPriceFeed            _feed;
    private readonly MifxCandleFeed           _candleFeed;
    private readonly MifxCommandQueue         _queue;
    private readonly MifxPositionSyncService  _syncService;
    private readonly ITradePositionRepository _positionRepo;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly IMediator                _mediator;
    private readonly ILogger<MifxBridgeController> _logger;

    public MifxBridgeController(
        MifxPriceFeed feed,
        MifxCandleFeed candleFeed,
        MifxCommandQueue queue,
        MifxPositionSyncService syncService,
        ITradePositionRepository positionRepo,
        IHubContext<DashboardHub> hub,
        IMediator mediator,
        ILogger<MifxBridgeController> logger)
    {
        _feed         = feed;
        _candleFeed   = candleFeed;
        _queue        = queue;
        _syncService  = syncService;
        _positionRepo = positionRepo;
        _hub          = hub;
        _mediator     = mediator;
        _logger       = logger;
    }

    // ── Dipanggil EA setiap detik: kirim bid/ask terbaru + posisi open ──

    [HttpPost("tick")]
    public async Task<IActionResult> ReceiveTick([FromBody] MifxTickRequest req)
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
            ATR14:          req.Atr14,
            ADX14:          req.Adx14,
            Support:        req.Support,
            Resistance:     req.Resistance);

        // Konversi DTO posisi ke domain value object (null = EA lama, tidak mengirim posisi)
        IReadOnlyList<MifxBrokerPosition>? brokerPositions = req.Positions?
            .Select(p => new MifxBrokerPosition(
                p.Ticket, p.Type, p.Symbol, p.Lots, p.OpenPrice, p.Profit, p.Pips))
            .ToList();

        _feed.Update(tick, brokerPositions);

        // Sync PnL + auto-close hanya jika EA mengirim field positions (EA v1.18+)
        if (req.Positions is not null)
            await _syncService.SyncAsync(brokerPositions ?? Array.Empty<MifxBrokerPosition>());

        // Broadcast ke dashboard via SignalR — best-effort, jangan blokir EA jika error
        try
        {
            var tickPayload = BuildStatusResponse();
            await _hub.Clients.All.SendAsync("tick", tickPayload);

            var positions = await _mediator.Send(new GetAllPositionsQuery());
            await _hub.Clients.All.SendAsync("positions", positions);

            var account = await _mediator.Send(new GetAccountHealthQuery());
            await _hub.Clients.All.SendAsync("account", account);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR broadcast gagal — dashboard akan fallback ke polling");
        }

        return Ok();
    }

    // ── Dipanggil EA saat detect ticket hilang dari open positions ─────────────
    // EA baca DEAL_PROFIT dari history → backend simpan realized profit yang akurat
    // (mencakup commission + swap + actual fill price, bukan estimasi dari tick terakhir).

    [HttpPost("closed-position")]
    public async Task<IActionResult> ReportClosedPosition([FromBody] MifxClosedPositionRequest req)
    {
        var externalId = $"MIFX-{req.Ticket}";
        var openPositions = await _positionRepo.GetOpenPositionsAsync();
        var position = openPositions.FirstOrDefault(p =>
            string.Equals(p.ExternalTradeId, externalId, StringComparison.OrdinalIgnoreCase));

        if (position is null)
        {
            _logger.LogInformation(
                "Closed-position report untuk ticket {Ticket} di-skip — posisi tidak ditemukan di state ACTIVE " +
                "(mungkin sudah ditutup manual dari dashboard atau MagicNumber mismatch)",
                req.Ticket);
            return Ok(new { skipped = true });
        }

        var closedAt = DateTimeOffset.FromUnixTimeSeconds(req.CloseTime);
        position.ClosedByBrokerWithProfit(req.NetProfit, req.ClosePrice, closedAt);
        await _positionRepo.SaveAsync(position);

        _logger.LogInformation(
            "Position {Id} ({Pair} {Dir}) closed dengan ACTUAL profit ${Net:F2} " +
            "(gross ${Gross:F2}, commission ${Comm:F2}, swap ${Swap:F2}) @ {Price} — outcome={Outcome}",
            position.TradeId, position.Pair, position.Direction,
            req.NetProfit, req.GrossProfit, req.Commission, req.Swap, req.ClosePrice, position.Status);

        // Broadcast positions + account terbaru ke dashboard
        try
        {
            var positions = await _mediator.Send(new GetAllPositionsQuery());
            await _hub.Clients.All.SendAsync("positions", positions);
            var account = await _mediator.Send(new GetAccountHealthQuery());
            await _hub.Clients.All.SendAsync("account", account);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR broadcast setelah closed-position gagal");
        }

        return Ok(new { tradeId = position.TradeId, netProfit = req.NetProfit });
    }

    // ── Dipanggil EA saat new bar / startup: kirim N candle terakhir untuk chart ──

    [HttpPost("candles")]
    public IActionResult IngestCandles(
        [FromQuery] string pair,
        [FromQuery] string timeframe,
        [FromBody]  List<MifxCandleDto> bars)
    {
        if (string.IsNullOrWhiteSpace(pair) || string.IsNullOrWhiteSpace(timeframe))
            return BadRequest(new { error = "pair dan timeframe wajib diisi" });
        if (bars is null || bars.Count == 0)
            return BadRequest(new { error = "body bars kosong" });

        var candles = bars
            .Select(b => new CandleBar(b.Time, b.Open, b.High, b.Low, b.Close, b.Volume))
            .ToList()
            .AsReadOnly();

        _candleFeed.Upsert(pair, timeframe, candles);

        _logger.LogDebug("MIFX candles ingested: {Pair} {TF} count={Count}",
            pair, timeframe, candles.Count);

        return Ok(new { count = candles.Count });
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
    public IActionResult GetStatus() => Ok(BuildStatusResponse());

    private object BuildStatusResponse()
    {
        var tick = _feed.Latest;
        return new
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
        };
    }
}
