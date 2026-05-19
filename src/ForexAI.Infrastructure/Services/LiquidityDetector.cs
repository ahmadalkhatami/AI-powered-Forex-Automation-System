using ForexAI.Domain.Enums;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

/// <summary>
/// Liquidity detection — protect against stop hunts (SL kena terus problem).
///
/// <para>Three concerns:</para>
/// <list type="number">
///   <item><b>Swing High/Low pools</b>: retail stops cluster above swing high / below swing low.
///         Smart money sweep these zones to grab liquidity sebelum reverse → SL hit.</item>
///   <item><b>Round Number magnets</b>: 1.1700 / 1.1650 / 1.1600 = obvious psychological levels.
///         Stops cluster here. Avoid entry within ±5p of round number.</item>
///   <item><b>Liquidity Sweep</b>: wick yang baru saja menembus swing level + close back inside.
///         Smart money just grabbed retail stops — opposite direction entry has high edge.</item>
/// </list>
/// </summary>
public static class LiquidityDetector
{
    public record SwingPoint(string Type, decimal Price, long FormedAt);

    public record SweepResult(
        bool Detected,
        string Direction,       // "Bullish" (low swept, BUY signal) | "Bearish" (high swept, SELL signal)
        decimal SweepLevel,
        decimal PipsBeyond,
        string Description);

    public record SlAdjustment(
        decimal OriginalSl,
        decimal AdjustedSl,
        decimal PushedPips,
        string Reason);

    private const decimal PipSize = 0.0001m;
    private const int SwingLookback = 3;  // pivot detection: high > N bar di kedua sisi
    private const decimal RoundMagnetPips = 5m;  // ±5 pip dari round number

    /// <summary>
    /// Cari semua swing high/low dalam window candles (pivot points).
    /// Pivot high: bar[i].High > bar[i±1..N].High
    /// Pivot low: bar[i].Low < bar[i±1..N].Low
    /// </summary>
    public static List<SwingPoint> FindSwingPoints(IReadOnlyList<CandleBar> candles, int lookback = 20)
    {
        var swings = new List<SwingPoint>();
        if (candles.Count < SwingLookback * 2 + 1) return swings;

        int start = Math.Max(SwingLookback, candles.Count - lookback);
        int end = candles.Count - SwingLookback;

        for (int i = start; i < end; i++)
        {
            bool isPivotHigh = true, isPivotLow = true;
            for (int j = 1; j <= SwingLookback; j++)
            {
                if (candles[i - j].High >= candles[i].High) isPivotHigh = false;
                if (candles[i + j].High >= candles[i].High) isPivotHigh = false;
                if (candles[i - j].Low  <= candles[i].Low)  isPivotLow  = false;
                if (candles[i + j].Low  <= candles[i].Low)  isPivotLow  = false;
            }

            if (isPivotHigh) swings.Add(new SwingPoint("SwingHigh", candles[i].High, candles[i].Time));
            if (isPivotLow)  swings.Add(new SwingPoint("SwingLow",  candles[i].Low,  candles[i].Time));
        }
        return swings;
    }

    /// <summary>
    /// Detect liquidity sweep di candle terbaru.
    /// Bullish sweep: candle.Low menembus 20-bar low + candle.Close back inside (close > 20-bar low).
    /// Bearish sweep: candle.High menembus 20-bar high + candle.Close back inside.
    /// Both = strong signal for OPPOSITE direction entry.
    /// </summary>
    public static SweepResult DetectSweep(IReadOnlyList<CandleBar> candles, int lookback = 20)
    {
        var none = new SweepResult(false, "None", 0m, 0m, "");
        if (candles.Count < lookback + 1) return none;

        var current = candles[^1];
        var prevBars = candles.Take(candles.Count - 1).TakeLast(lookback).ToList();
        decimal highest = prevBars.Max(b => b.High);
        decimal lowest  = prevBars.Min(b => b.Low);

        // Bullish sweep: wick down menembus prev low, close back inside
        if (current.Low < lowest && current.Close > lowest)
        {
            decimal pipsBeyond = Math.Round((lowest - current.Low) / PipSize, 1);
            if (pipsBeyond < 1m) return none;  // skip < 1p noise
            return new SweepResult(
                Detected: true,
                Direction: "Bullish",
                SweepLevel: lowest,
                PipsBeyond: pipsBeyond,
                Description: $"Bullish sweep: low {pipsBeyond}p di bawah 20-bar low ({lowest:F5}), close back inside — stops grabbed, reversal probable");
        }

        // Bearish sweep: wick up menembus prev high, close back inside
        if (current.High > highest && current.Close < highest)
        {
            decimal pipsBeyond = Math.Round((current.High - highest) / PipSize, 1);
            if (pipsBeyond < 1m) return none;
            return new SweepResult(
                Detected: true,
                Direction: "Bearish",
                SweepLevel: highest,
                PipsBeyond: pipsBeyond,
                Description: $"Bearish sweep: high {pipsBeyond}p di atas 20-bar high ({highest:F5}), close back inside — stops grabbed, reversal probable");
        }

        return none;
    }

    /// <summary>
    /// Check apakah price dekat round number magnet (within ±5 pip dari 50/100 pip level).
    /// EURUSD: 1.16500, 1.17000, dst. Pakai 50-pip step.
    /// </summary>
    public static bool IsNearRoundNumber(decimal price, out decimal nearestRound, out decimal pipsAway)
    {
        // Round to nearest 50-pip level (0.0050)
        decimal stepSize = 50m * PipSize;
        nearestRound = Math.Round(price / stepSize) * stepSize;
        pipsAway = Math.Abs(price - nearestRound) / PipSize;
        return pipsAway <= RoundMagnetPips;
    }

    /// <summary>
    /// Defensive SL placement — kalau planned SL terlalu dekat swing level (≤5p),
    /// push 3p beyond level supaya retail stop hunt tidak ambil posisi kita.
    /// Return null kalau tidak perlu adjust.
    /// </summary>
    public static SlAdjustment? AdjustStopLoss(
        decimal originalSl, decimal entry, SignalDirection direction,
        List<SwingPoint> swings)
    {
        if (swings.Count == 0) return null;

        const decimal proximityPips = 5m;
        const decimal pushBeyondPips = 3m;
        decimal proximityPrice = proximityPips * PipSize;
        decimal pushPrice = pushBeyondPips * PipSize;

        if (direction == SignalDirection.BUY)
        {
            // BUY → SL di bawah entry. Cari swing low yang DI ATAS SL (in our risk zone).
            // Kalau ada swing low yang dekat dengan SL kita (jarak ≤ 5p), push SL ke bawah swing low.
            var nearestSwingLow = swings
                .Where(s => s.Type == "SwingLow" && s.Price < entry && s.Price > originalSl - proximityPrice)
                .OrderBy(s => Math.Abs(s.Price - originalSl))
                .FirstOrDefault();
            if (nearestSwingLow == null) return null;

            decimal pushedSl = nearestSwingLow.Price - pushPrice;
            if (pushedSl >= originalSl) return null;  // SL kita sudah lebih lebar, no adjust

            decimal pushedPips = Math.Round((originalSl - pushedSl) / PipSize, 1);
            return new SlAdjustment(
                originalSl, Math.Round(pushedSl, 5), pushedPips,
                $"SL push {pushedPips}p untuk hindari swing low @ {nearestSwingLow.Price:F5} (retail stop magnet)");
        }
        else if (direction == SignalDirection.SELL)
        {
            // SELL → SL di atas entry. Cari swing high yang DI BAWAH SL.
            var nearestSwingHigh = swings
                .Where(s => s.Type == "SwingHigh" && s.Price > entry && s.Price < originalSl + proximityPrice)
                .OrderBy(s => Math.Abs(s.Price - originalSl))
                .FirstOrDefault();
            if (nearestSwingHigh == null) return null;

            decimal pushedSl = nearestSwingHigh.Price + pushPrice;
            if (pushedSl <= originalSl) return null;

            decimal pushedPips = Math.Round((pushedSl - originalSl) / PipSize, 1);
            return new SlAdjustment(
                originalSl, Math.Round(pushedSl, 5), pushedPips,
                $"SL push {pushedPips}p untuk hindari swing high @ {nearestSwingHigh.Price:F5} (retail stop magnet)");
        }
        return null;
    }
}
