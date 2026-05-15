using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure.Mifx;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/market")]
public class MarketController : ControllerBase
{
    private readonly MifxCandleFeed _feed;

    public MarketController(MifxCandleFeed feed) => _feed = feed;

    [HttpGet("candles")]
    public ActionResult<IReadOnlyList<CandleBar>> GetCandles(
        [FromQuery] string pair = "EURUSD",
        [FromQuery] string timeframe = "M15",
        [FromQuery] int count = 200,
        CancellationToken ct = default)
    {
        var tf = timeframe.ToUpperInvariant();
        if (tf is not ("M15" or "H1" or "D1"))
            return BadRequest(new { error = $"Timeframe '{timeframe}' tidak didukung. Pakai M15, H1, atau D1." });

        var candles = _feed.Get(pair, tf, count);
        return Ok(candles);
    }
}
