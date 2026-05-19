using ForexAI.Infrastructure.Mifx;
using ForexAI.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/pattern")]
public class PatternController : ControllerBase
{
    private readonly MifxCandleFeed _candleFeed;

    public PatternController(MifxCandleFeed candleFeed)
    {
        _candleFeed = candleFeed;
    }

    /// <summary>
    /// Deteksi candlestick pattern terbaru untuk pair tertentu di multiple TF.
    /// Return pattern name, bias, reliability, dan candle time(s) yang form pattern
    /// (untuk overlay di chart frontend).
    /// </summary>
    [HttpGet("detect")]
    public ActionResult<PatternDetectionResponse> Detect([FromQuery] string pair = "EURUSD")
    {
        var result = new PatternDetectionResponse(
            Pair: pair,
            M15: DetectForTimeframe(pair, "M15"),
            H1:  DetectForTimeframe(pair, "H1"),
            D1:  DetectForTimeframe(pair, "D1"));
        return Ok(result);
    }

    private TimeframePattern DetectForTimeframe(string pair, string timeframe)
    {
        var candles = _candleFeed.Get(pair, timeframe, 3);
        if (candles.Count == 0)
            return new TimeframePattern("None", "Neutral", 0m, "Belum ada candle data", Array.Empty<long>());

        var pat = CandlestickPatternDetector.Detect(candles);
        if (pat.Name == "None")
            return new TimeframePattern("None", "Neutral", 0m, "Tidak ada pattern", Array.Empty<long>());

        // Pattern multi-candle: include semua candle time yang relevan
        // (Star = 3, Engulfing/InsideBar = 2, Pin/Doji/Marubozu = 1)
        int candleCount = pat.Name.Contains("Star") ? 3 :
                          (pat.Name.Contains("Engulfing") || pat.Name.Contains("Inside")) ? 2 : 1;
        var times = candles.TakeLast(candleCount).Select(c => c.Time).ToArray();
        return new TimeframePattern(pat.Name, pat.Bias, pat.Reliability, pat.Description, times);
    }

    /// <summary>
    /// Fair Value Gap (FVG) detection per-TF. Return semua zone yang formed dalam
    /// 50 bar terakhir, dengan flag filled/unfilled. Unfilled FVG = actionable level.
    /// </summary>
    [HttpGet("fvg")]
    public ActionResult<FvgDetectionResponse> Fvg([FromQuery] string pair = "EURUSD")
    {
        var result = new FvgDetectionResponse(
            Pair: pair,
            M15: DetectFvgForTimeframe(pair, "M15"),
            H1:  DetectFvgForTimeframe(pair, "H1"),
            D1:  DetectFvgForTimeframe(pair, "D1"));
        return Ok(result);
    }

    private FvgZoneDto[] DetectFvgForTimeframe(string pair, string timeframe)
    {
        var candles = _candleFeed.Get(pair, timeframe, 60);
        if (candles.Count < 3) return Array.Empty<FvgZoneDto>();

        var zones = FairValueGapDetector.Detect(candles);
        return zones.Select(z => new FvgZoneDto(
            Bias: z.Bias,
            Top: z.Top,
            Bottom: z.Bottom,
            FormedAt: z.FormedAt,
            ExpiresAfter: z.ExpiresAfter,
            Filled: z.Filled,
            SizePips: z.SizePips)).ToArray();
    }

    /// <summary>
    /// Order Block (OB) detection per-TF. Last opposite-direction candle sebelum
    /// strong impulse move — SMC zone yang sering di-mitigate sebelum continuation.
    /// Return semua OB dari 60 bar terakhir + flag mitigated/unmitigated.
    /// </summary>
    [HttpGet("orderblock")]
    public ActionResult<OrderBlockResponse> OrderBlocks([FromQuery] string pair = "EURUSD")
    {
        var result = new OrderBlockResponse(
            Pair: pair,
            M15: DetectObForTimeframe(pair, "M15"),
            H1:  DetectObForTimeframe(pair, "H1"),
            D1:  DetectObForTimeframe(pair, "D1"));
        return Ok(result);
    }

    private OrderBlockDto[] DetectObForTimeframe(string pair, string timeframe)
    {
        var candles = _candleFeed.Get(pair, timeframe, 60);
        if (candles.Count < 5) return Array.Empty<OrderBlockDto>();

        var blocks = OrderBlockDetector.Detect(candles);
        return blocks.Select(b => new OrderBlockDto(
            Bias:      b.Bias,
            Top:       b.Top,
            Bottom:    b.Bottom,
            FormedAt:  b.FormedAt,
            SizePips:  b.SizePips,
            Mitigated: b.Mitigated)).ToArray();
    }
}

public record PatternDetectionResponse(
    string Pair,
    TimeframePattern M15,
    TimeframePattern H1,
    TimeframePattern D1);

public record TimeframePattern(
    string Name,
    string Bias,
    decimal Reliability,
    string Description,
    long[] CandleTimes);

public record FvgDetectionResponse(
    string Pair,
    FvgZoneDto[] M15,
    FvgZoneDto[] H1,
    FvgZoneDto[] D1);

public record FvgZoneDto(
    string Bias,
    decimal Top,
    decimal Bottom,
    long FormedAt,
    long ExpiresAfter,
    bool Filled,
    decimal SizePips);

public record OrderBlockResponse(
    string Pair,
    OrderBlockDto[] M15,
    OrderBlockDto[] H1,
    OrderBlockDto[] D1);

public record OrderBlockDto(
    string Bias,             // Bullish / Bearish
    decimal Top,
    decimal Bottom,
    long FormedAt,
    decimal SizePips,
    bool Mitigated);         // true = price sudah revisit zone
