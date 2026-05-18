using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

/// <summary>
/// Deteksi candlestick pattern klasik dari last 1-3 candle.
/// Return name pattern + bias (bullish/bearish/neutral) + reliability 0-1.
///
/// <para>Patterns yang di-detect:</para>
/// <list type="bullet">
///   <item>Pin Bar (Bullish/Bearish) — long wick reversal</item>
///   <item>Engulfing (Bullish/Bearish) — body covers prior</item>
///   <item>Doji — indecision, small body</item>
///   <item>Hammer / Shooting Star — context-aware pin bar</item>
///   <item>Inside Bar — consolidation, range within prior</item>
///   <item>Marubozu (Bullish/Bearish) — strong directional, no wicks</item>
///   <item>Morning/Evening Star — 3-candle reversal</item>
/// </list>
/// </summary>
public static class CandlestickPatternDetector
{
    public record PatternResult(
        string Name,           // "Bullish Pin Bar", "Engulfing", dll. "None" kalau tidak ada
        string Bias,           // "Bullish", "Bearish", "Neutral"
        decimal Reliability,   // 0..1, indikator strength
        string Description);   // 1-line untuk display

    public static PatternResult Detect(IReadOnlyList<CandleBar> candles)
    {
        if (candles.Count == 0) return new PatternResult("None", "Neutral", 0m, "");

        var c = candles[^1];  // current
        var p1 = candles.Count >= 2 ? candles[^2] : null;
        var p2 = candles.Count >= 3 ? candles[^3] : null;

        // ── Multi-candle patterns dulu (3 candle stars > 2 candle engulf > 1 candle pin) ──
        if (p1 != null && p2 != null)
        {
            var star = TryStar(p2, p1, c);
            if (star.Name != "None") return star;
        }

        if (p1 != null)
        {
            var eng = TryEngulfing(p1, c);
            if (eng.Name != "None") return eng;

            var inside = TryInsideBar(p1, c);
            if (inside.Name != "None") return inside;
        }

        // Single-candle patterns (urut by reliability)
        var marubozu = TryMarubozu(c);
        if (marubozu.Name != "None") return marubozu;

        var pin = TryPinBar(c);
        if (pin.Name != "None") return pin;

        var doji = TryDoji(c);
        if (doji.Name != "None") return doji;

        return new PatternResult("None", "Neutral", 0m, "Tidak ada pattern spesifik");
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static decimal Body(CandleBar b)  => Math.Abs(b.Close - b.Open);
    private static decimal Range(CandleBar b) => b.High - b.Low;
    private static decimal UpperWick(CandleBar b) => b.High - Math.Max(b.Open, b.Close);
    private static decimal LowerWick(CandleBar b) => Math.Min(b.Open, b.Close) - b.Low;
    private static bool IsBullish(CandleBar b) => b.Close > b.Open;
    private static bool IsBearish(CandleBar b) => b.Close < b.Open;

    // ── Pin Bar — long wick (≥2× body), small body in opposite end ───────
    private static PatternResult TryPinBar(CandleBar c)
    {
        decimal body = Body(c), range = Range(c);
        if (range <= 0m) return None();
        decimal bodyRatio = body / range;
        decimal upperWick = UpperWick(c), lowerWick = LowerWick(c);

        // Bullish pin: long lower wick (rejection bottom), body in upper third
        if (lowerWick >= 2m * body && bodyRatio < 0.35m && upperWick < body)
            return new PatternResult("Bullish Pin Bar", "Bullish", 0.75m,
                $"Long lower wick (rejection low) — bullish reversal");

        // Bearish pin: long upper wick (rejection top), body in lower third
        if (upperWick >= 2m * body && bodyRatio < 0.35m && lowerWick < body)
            return new PatternResult("Bearish Pin Bar", "Bearish", 0.75m,
                $"Long upper wick (rejection high) — bearish reversal");

        return None();
    }

    // ── Engulfing — current body fully covers prior body, opposite color ─
    private static PatternResult TryEngulfing(CandleBar prior, CandleBar c)
    {
        decimal priorBody = Body(prior), curBody = Body(c);
        if (curBody < priorBody * 1.1m) return None();   // butuh lebih besar 10%+

        // Bullish engulfing: current green, fully covers prior red body
        if (IsBullish(c) && IsBearish(prior) &&
            c.Open <= prior.Close && c.Close >= prior.Open)
            return new PatternResult("Bullish Engulfing", "Bullish", 0.80m,
                "Body bullish menelan body bearish sebelumnya — strong reversal");

        // Bearish engulfing: current red, fully covers prior green body
        if (IsBearish(c) && IsBullish(prior) &&
            c.Open >= prior.Close && c.Close <= prior.Open)
            return new PatternResult("Bearish Engulfing", "Bearish", 0.80m,
                "Body bearish menelan body bullish sebelumnya — strong reversal");

        return None();
    }

    // ── Doji — very small body (< 10% range), indecision ──────────────────
    private static PatternResult TryDoji(CandleBar c)
    {
        decimal body = Body(c), range = Range(c);
        if (range <= 0m) return None();
        if (body / range < 0.10m)
            return new PatternResult("Doji", "Neutral", 0.50m,
                "Body tipis, market indecision — tunggu konfirmasi candle berikutnya");
        return None();
    }

    // ── Marubozu — full body, no wicks (or tiny). Strong directional. ────
    private static PatternResult TryMarubozu(CandleBar c)
    {
        decimal body = Body(c), range = Range(c);
        if (range <= 0m) return None();
        // Body ≥ 95% of range = effectively no wick
        if (body / range < 0.95m) return None();
        if (IsBullish(c))
            return new PatternResult("Bullish Marubozu", "Bullish", 0.70m,
                "Body bullish penuh tanpa wick — strong momentum");
        if (IsBearish(c))
            return new PatternResult("Bearish Marubozu", "Bearish", 0.70m,
                "Body bearish penuh tanpa wick — strong momentum");
        return None();
    }

    // ── Inside Bar — current range fully inside prior range (consolidation) ─
    private static PatternResult TryInsideBar(CandleBar prior, CandleBar c)
    {
        if (c.High <= prior.High && c.Low >= prior.Low)
            return new PatternResult("Inside Bar", "Neutral", 0.45m,
                "Range di dalam candle sebelumnya — consolidation, tunggu breakout");
        return None();
    }

    // ── Morning/Evening Star (3-candle reversal) ─────────────────────────
    // Morning Star (bullish): bearish big body → small body (any) → bullish big body
    // Evening Star (bearish): bullish big body → small body (any) → bearish big body
    private static PatternResult TryStar(CandleBar c1, CandleBar c2, CandleBar c3)
    {
        decimal body1 = Body(c1), body2 = Body(c2), body3 = Body(c3);
        decimal r1 = Range(c1), r2 = Range(c2), r3 = Range(c3);
        if (r1 <= 0m || r2 <= 0m || r3 <= 0m) return None();

        // C2 must be a small body (star itself)
        if (body2 / r2 > 0.30m) return None();
        // C1 dan C3 must be substantial bodies
        if (body1 / r1 < 0.50m || body3 / r3 < 0.50m) return None();

        // Morning Star: bearish, small, bullish (close > midpoint of c1 body)
        if (IsBearish(c1) && IsBullish(c3) && c3.Close > (c1.Open + c1.Close) / 2m)
            return new PatternResult("Morning Star", "Bullish", 0.85m,
                "3-candle bullish reversal: down → indecision → up — high reliability");

        // Evening Star: bullish, small, bearish (close < midpoint of c1 body)
        if (IsBullish(c1) && IsBearish(c3) && c3.Close < (c1.Open + c1.Close) / 2m)
            return new PatternResult("Evening Star", "Bearish", 0.85m,
                "3-candle bearish reversal: up → indecision → down — high reliability");

        return None();
    }

    private static PatternResult None() => new("None", "Neutral", 0m, "");
}
