using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

/// <summary>
/// Fair Value Gap (FVG) detector — Smart Money Concepts.
/// <para>
/// FVG = 3-candle pattern where wick gap exists:
/// </para>
/// <list type="bullet">
///   <item><b>Bullish FVG</b>: low[N] > high[N-2] — strong upward push leaves untraded zone.
///         Price statistik retrace ~80% kasus untuk fill gap → continue. Demand zone.</item>
///   <item><b>Bearish FVG</b>: high[N] &lt; low[N-2] — strong downward push leaves gap.
///         Supply zone — price retest sebelum continue down.</item>
/// </list>
/// <para>
/// Tracking semua FVG di window 50 bar terakhir, mark "filled" kalau price sudah retrace ke
/// dalam zone. Unfilled FVG = actionable level untuk entry/exit decision.
/// </para>
/// </summary>
public static class FairValueGapDetector
{
    public record FvgZone(
        string Bias,              // "Bullish" atau "Bearish"
        decimal Top,              // Upper edge of gap
        decimal Bottom,           // Lower edge of gap
        long FormedAt,            // Unix timestamp candle ke-3 (close yang form gap)
        long ExpiresAfter,        // FormedAt + (50 × barDuration) — fallback expiry
        bool Filled,              // True kalau price sudah retrace ke dalam zone
        decimal SizePips);        // Gap height dalam pip (0.0001 = 1 pip EURUSD)

    /// <summary>
    /// Scan last N candles, return semua FVG (filled + unfilled) yang masih dalam window.
    /// Unfilled = price belum retrace ke dalam zone setelah formation.
    /// </summary>
    public static List<FvgZone> Detect(IReadOnlyList<CandleBar> candles, int lookback = 50)
    {
        var zones = new List<FvgZone>();
        if (candles.Count < 3) return zones;

        long barDur = candles.Count >= 2 ? candles[^1].Time - candles[^2].Time : 900;
        int start = Math.Max(2, candles.Count - lookback);
        const decimal pipSize = 0.0001m;

        for (int i = start; i < candles.Count; i++)
        {
            var c1 = candles[i - 2];
            var c3 = candles[i];

            // Bullish FVG: low[i] > high[i-2] (gap di tengah candle i-1)
            if (c3.Low > c1.High)
            {
                decimal gap = c3.Low - c1.High;
                if (gap < 0.5m * pipSize) continue;  // skip noise < 0.5 pip
                bool filled = IsFilled(candles, i, c3.Low, c1.High, isBullish: true);
                zones.Add(new FvgZone(
                    Bias: "Bullish",
                    Top: c3.Low,
                    Bottom: c1.High,
                    FormedAt: c3.Time,
                    ExpiresAfter: c3.Time + 50 * barDur,
                    Filled: filled,
                    SizePips: Math.Round(gap / pipSize, 1)));
            }
            // Bearish FVG: high[i] < low[i-2]
            else if (c3.High < c1.Low)
            {
                decimal gap = c1.Low - c3.High;
                if (gap < 0.5m * pipSize) continue;
                bool filled = IsFilled(candles, i, c1.Low, c3.High, isBullish: false);
                zones.Add(new FvgZone(
                    Bias: "Bearish",
                    Top: c1.Low,
                    Bottom: c3.High,
                    FormedAt: c3.Time,
                    ExpiresAfter: c3.Time + 50 * barDur,
                    Filled: filled,
                    SizePips: Math.Round(gap / pipSize, 1)));
            }
        }

        return zones;
    }

    /// <summary>Filled kalau ada candle setelah formation yang trade ke dalam zone.</summary>
    private static bool IsFilled(IReadOnlyList<CandleBar> candles, int formedIdx, decimal top, decimal bottom, bool isBullish)
    {
        for (int j = formedIdx + 1; j < candles.Count; j++)
        {
            var c = candles[j];
            // Bullish FVG fill: price retrace ke dalam gap (low < top of gap)
            // Bearish FVG fill: price retrace ke dalam gap (high > bottom of gap)
            if (isBullish && c.Low <= top) return true;
            if (!isBullish && c.High >= bottom) return true;
        }
        return false;
    }
}
