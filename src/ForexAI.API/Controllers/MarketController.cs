using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/market")]
public class MarketController : ControllerBase
{
    private readonly ICandleDataService _candles;

    public MarketController(ICandleDataService candles) => _candles = candles;

    [HttpGet("candles")]
    public async Task<ActionResult<IReadOnlyList<CandleBar>>> GetCandles(
        [FromQuery] string pair = "EURUSD",
        [FromQuery] int count = 90,
        CancellationToken ct = default)
    {
        var result = await _candles.GetCandlesAsync(pair, count);
        return Ok(result);
    }
}
