using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

/// <summary>
/// Detect Break of Structure (BOS) + Change of Character (CHoCH) dari swing pivots
/// dan candle history.
///
/// <para>Terminology:</para>
/// <list type="bullet">
///   <item><b>BOS Bullish</b>: dalam uptrend (consecutive Higher Highs), price close di atas
///         latest swing high → continuation signal</item>
///   <item><b>BOS Bearish</b>: dalam downtrend (Lower Lows), price close di bawah
///         latest swing low → continuation</item>
///   <item><b>CHoCH Bullish</b>: dalam downtrend, price close di atas latest LOWER HIGH
///         → potential trend reversal up</item>
///   <item><b>CHoCH Bearish</b>: dalam uptrend, price close di bawah latest HIGHER LOW
///         → potential reversal down</item>
/// </list>
///
/// <para>Hanya detect break terbaru (recent N bar) untuk avoid menampilkan history lama.</para>
/// </summary>
public static class StructureBreakDetector
{
    /// <param name="Type">BOS_Bullish / BOS_Bearish / CHoCH_Bullish / CHoCH_Bearish</param>
    /// <param name="BrokenLevel">Price level yang di-break (swing high atau swing low)</param>
    /// <param name="LevelFormedAt">Time when broken swing was formed</param>
    /// <param name="BrokenAtTime">Time candle yang break level</param>
    /// <param name="Significance">Major / Minor — Major berdasarkan trend kuat sebelumnya</param>
    public record BreakEvent(
        string Type,
        decimal BrokenLevel,
        long LevelFormedAt,
        long BrokenAtTime,
        string Significance);

    public static List<BreakEvent> Detect(
        IReadOnlyList<LiquidityDetector.SwingPoint> swings,
        IReadOnlyList<CandleBar> candles,
        int lookbackBars = 30)
    {
        var events = new List<BreakEvent>();
        if (swings.Count < 2 || candles.Count < 2) return events;

        var sorted = swings.OrderBy(s => s.FormedAt).ToList();
        var highs = sorted.Where(s => s.Type == "SwingHigh").ToList();
        var lows  = sorted.Where(s => s.Type == "SwingLow").ToList();

        // Limit candles ke lookbackBars terakhir supaya hanya break recent yang di-emit
        var recent = candles.Count <= lookbackBars
            ? candles
            : candles.Skip(candles.Count - lookbackBars).ToList();

        // ── Check break ke atas (bullish) ─────────────────────────────────
        var lastHigh = highs.LastOrDefault();
        if (lastHigh != null)
        {
            var breakBar = recent.FirstOrDefault(c =>
                c.Time > lastHigh.FormedAt && c.Close > lastHigh.Price);
            if (breakBar != null)
            {
                bool inDowntrend = IsHighsDescending(highs);
                string type = inDowntrend ? "CHoCH_Bullish" : "BOS_Bullish";
                string sig = inDowntrend ? "Major" : "Minor";  // reversal lebih significant
                events.Add(new BreakEvent(type, lastHigh.Price, lastHigh.FormedAt, breakBar.Time, sig));
            }
        }

        // ── Check break ke bawah (bearish) ────────────────────────────────
        var lastLow = lows.LastOrDefault();
        if (lastLow != null)
        {
            var breakBar = recent.FirstOrDefault(c =>
                c.Time > lastLow.FormedAt && c.Close < lastLow.Price);
            if (breakBar != null)
            {
                bool inUptrend = IsLowsAscending(lows);
                string type = inUptrend ? "CHoCH_Bearish" : "BOS_Bearish";
                string sig = inUptrend ? "Major" : "Minor";
                events.Add(new BreakEvent(type, lastLow.Price, lastLow.FormedAt, breakBar.Time, sig));
            }
        }

        return events;
    }

    /// <summary>True kalau 2 swing high terakhir adalah Lower Highs (downtrend).</summary>
    private static bool IsHighsDescending(List<LiquidityDetector.SwingPoint> highs)
    {
        if (highs.Count < 2) return false;
        var lastTwo = highs.TakeLast(2).ToList();
        return lastTwo[1].Price < lastTwo[0].Price;
    }

    /// <summary>True kalau 2 swing low terakhir adalah Higher Lows (uptrend).</summary>
    private static bool IsLowsAscending(List<LiquidityDetector.SwingPoint> lows)
    {
        if (lows.Count < 2) return false;
        var lastTwo = lows.TakeLast(2).ToList();
        return lastTwo[1].Price > lastTwo[0].Price;
    }
}
