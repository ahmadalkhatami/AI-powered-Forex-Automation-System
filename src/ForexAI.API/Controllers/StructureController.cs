using ForexAI.Infrastructure.Mifx;
using ForexAI.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/structure")]
public class StructureController : ControllerBase
{
    private readonly MifxCandleFeed _candleFeed;

    public StructureController(MifxCandleFeed candleFeed)
    {
        _candleFeed = candleFeed;
    }

    /// <summary>
    /// Dynamic structure — swing pivots + derived trendlines (Dynamic Resistance / Support).
    ///
    /// <para>Trendline derived dari 2 swing point terakhir di tiap arah:</para>
    /// <list type="bullet">
    ///   <item>Dynamic Resistance: line connecting last 2 swing highs (descending = bearish channel; ascending = uptrend extension)</item>
    ///   <item>Dynamic Support: line connecting last 2 swing lows</item>
    /// </list>
    ///
    /// <para>FE render sebagai dashed trendline + small pivot markers, extend ke kanan
    /// untuk projection ke price action berikutnya.</para>
    /// </summary>
    [HttpGet("dynamic")]
    public ActionResult<DynamicStructureResponse> DynamicStructure(
        [FromQuery] string pair = "EURUSD",
        [FromQuery] string timeframe = "M15")
    {
        var candles = _candleFeed.Get(pair, timeframe, 100);
        if (candles.Count < 10)
        {
            return Ok(new DynamicStructureResponse(
                Pair: pair,
                Timeframe: timeframe,
                SwingHighs: Array.Empty<SwingPointDto>(),
                SwingLows: Array.Empty<SwingPointDto>(),
                DynamicResistance: null,
                DynamicSupport: null,
                BreakEvents: Array.Empty<BreakEventDto>()));
        }

        // Lookback 80 bar — capture history yang cukup untuk trendline meaningful.
        var swings = LiquidityDetector.FindSwingPoints(candles, lookback: 80);

        var highs = swings
            .Where(s => s.Type == "SwingHigh")
            .OrderBy(s => s.FormedAt)
            .Select(s => new SwingPointDto("High", s.Price, s.FormedAt))
            .ToList();
        var lows = swings
            .Where(s => s.Type == "SwingLow")
            .OrderBy(s => s.FormedAt)
            .Select(s => new SwingPointDto("Low", s.Price, s.FormedAt))
            .ToList();

        var resistance = BuildTrendline(highs.TakeLast(3).ToList(), candles[^1].Time);
        var support    = BuildTrendline(lows.TakeLast(3).ToList(), candles[^1].Time);

        // BOS/CHoCH detection — based on swing structure + recent candles
        var breaks = StructureBreakDetector.Detect(swings, candles, lookbackBars: 30);
        var breakDtos = breaks.Select(b => new BreakEventDto(
            Type: b.Type,
            BrokenLevel: b.BrokenLevel,
            LevelFormedAt: b.LevelFormedAt,
            BrokenAtTime: b.BrokenAtTime,
            Significance: b.Significance)).ToArray();

        return Ok(new DynamicStructureResponse(
            Pair: pair,
            Timeframe: timeframe,
            SwingHighs: highs.TakeLast(5).ToArray(),
            SwingLows: lows.TakeLast(5).ToArray(),
            DynamicResistance: resistance,
            DynamicSupport: support,
            BreakEvents: breakDtos));
    }

    /// <summary>
    /// Build trendline dari 2-3 swing point terakhir.
    /// Format: 2 endpoints (start swing, current bar projection) + slope info.
    ///
    /// <para>Untuk 3 swing point: pakai 2 terakhir untuk slope, ke-3 dipakai sebagai "anchor" historical.
    /// Untuk 2 swing point: connect langsung.
    /// Untuk &lt; 2 swing point: tidak bisa build trendline (return null).</para>
    /// </summary>
    private static TrendlineDto? BuildTrendline(List<SwingPointDto> points, long currentBarTime)
    {
        if (points.Count < 2) return null;

        // Pakai 2 swing terakhir untuk derive slope
        var p1 = points[^2];
        var p2 = points[^1];

        long dtSec = p2.Time - p1.Time;
        if (dtSec <= 0) return null;

        decimal slopePerSec = (p2.Price - p1.Price) / dtSec;

        // Project line ke current bar time (extend ke kanan)
        long projectionTime = Math.Max(p2.Time, currentBarTime);
        decimal projectionPrice = p2.Price + slopePerSec * (projectionTime - p2.Time);

        // Slope direction label
        string direction = slopePerSec > 0 ? "Ascending" : slopePerSec < 0 ? "Descending" : "Flat";

        // Strength heuristic: 3-point linear fit error (kalau ada 3 swing, cek apakah line consistent)
        string strength = "Good";
        if (points.Count >= 3)
        {
            var p0 = points[^3];
            decimal expectedAtP0 = p1.Price + slopePerSec * (p0.Time - p1.Time);
            decimal errPips = Math.Abs(p0.Price - expectedAtP0) / 0.0001m;
            strength = errPips < 5m ? "Strong" : errPips < 15m ? "Good" : "Weak";
        }

        return new TrendlineDto(
            StartTime:  p1.Time,
            StartPrice: p1.Price,
            EndTime:    projectionTime,
            EndPrice:   Math.Round(projectionPrice, 5),
            Direction:  direction,
            Strength:   strength,
            SlopePipsPerHour: Math.Round(slopePerSec * 3600m / 0.0001m, 2));
    }
}

public record DynamicStructureResponse(
    string Pair,
    string Timeframe,
    SwingPointDto[] SwingHighs,
    SwingPointDto[] SwingLows,
    TrendlineDto? DynamicResistance,
    TrendlineDto? DynamicSupport,
    BreakEventDto[] BreakEvents);

public record SwingPointDto(string Type, decimal Price, long Time);

public record TrendlineDto(
    long StartTime,
    decimal StartPrice,
    long EndTime,
    decimal EndPrice,
    string Direction,        // Ascending / Descending / Flat
    string Strength,         // Strong (3+ swing aligned) / Good / Weak
    decimal SlopePipsPerHour);

/// <summary>BOS / CHoCH break event detected oleh StructureBreakDetector.</summary>
public record BreakEventDto(
    string Type,             // BOS_Bullish / BOS_Bearish / CHoCH_Bullish / CHoCH_Bearish
    decimal BrokenLevel,
    long LevelFormedAt,
    long BrokenAtTime,
    string Significance);    // Major / Minor
