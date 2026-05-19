using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

/// <summary>
/// Order Block (OB) detection — last opposite-direction candle before strong impulse move.
/// SMC concept: candle range becomes key zone where institutions accumulated, price often
/// returns to "mitigate" sebelum lanjut move.
///
/// <para>Detection criteria (simplified — sufficient untuk M15/H1 swing trading):</para>
/// <list type="number">
///   <item><b>Bullish OB</b>: bearish candle[i] followed by 3 bullish candle[i+1..i+3] yang push
///         price &gt; candle[i].High. Range candle[i].Low..candle[i].High = OB zone.</item>
///   <item><b>Bearish OB</b>: bullish candle[i] followed by 3 bearish candle[i+1..i+3] yang push
///         price &lt; candle[i].Low. Range candle[i].Low..candle[i].High = OB zone.</item>
/// </list>
///
/// <para>Mitigated = price sudah revisit OB zone post-formation. Unmitigated OB =
/// actionable zone yang masih "fresh".</para>
/// </summary>
public static class OrderBlockDetector
{
    private const int MinImpulseBars = 3;        // butuh ≥ 3 candle searah setelah OB
    private const decimal MinImpulsePips = 10m;  // total move impuls ≥ 10 pip (filter chop)
    private const decimal PipSize = 0.0001m;

    public record OrderBlock(
        string Bias,        // "Bullish" / "Bearish"
        decimal Top,
        decimal Bottom,
        long FormedAt,
        decimal SizePips,
        bool Mitigated);    // true = price sudah revisit zone

    public static List<OrderBlock> Detect(IReadOnlyList<CandleBar> candles)
    {
        var blocks = new List<OrderBlock>();
        if (candles.Count < MinImpulseBars + 2) return blocks;

        // Iterate candidate OB candles — lewati bar terakhir N karena belum ada impulse data
        for (int i = 1; i < candles.Count - MinImpulseBars; i++)
        {
            var ob = candles[i];
            var imp1 = candles[i + 1];
            var imp2 = candles[i + 2];
            var imp3 = candles[i + 3];

            bool obBearish = ob.Close < ob.Open;
            bool obBullish = ob.Close > ob.Open;

            // ── Bullish OB pattern ────────────────────────────────────────
            // bearish OB + 3 bullish impulse + closing breakout above OB high
            if (obBearish &&
                imp1.Close > imp1.Open && imp2.Close > imp2.Open && imp3.Close > imp3.Open &&
                imp3.Close > ob.High)
            {
                decimal impulsePips = (imp3.Close - ob.Close) / PipSize;
                if (impulsePips < MinImpulsePips) continue;

                // Mitigated kalau price sudah revisit (low touch atau lewat zone)
                bool mitigated = candles.Skip(i + MinImpulseBars + 1)
                    .Any(c => c.Low <= ob.High && c.High >= ob.Low);

                blocks.Add(new OrderBlock(
                    Bias:     "Bullish",
                    Top:      ob.High,
                    Bottom:   ob.Low,
                    FormedAt: ob.Time,
                    SizePips: Math.Round((ob.High - ob.Low) / PipSize, 1),
                    Mitigated: mitigated));
            }
            // ── Bearish OB pattern ────────────────────────────────────────
            else if (obBullish &&
                     imp1.Close < imp1.Open && imp2.Close < imp2.Open && imp3.Close < imp3.Open &&
                     imp3.Close < ob.Low)
            {
                decimal impulsePips = (ob.Close - imp3.Close) / PipSize;
                if (impulsePips < MinImpulsePips) continue;

                bool mitigated = candles.Skip(i + MinImpulseBars + 1)
                    .Any(c => c.Low <= ob.High && c.High >= ob.Low);

                blocks.Add(new OrderBlock(
                    Bias:     "Bearish",
                    Top:      ob.High,
                    Bottom:   ob.Low,
                    FormedAt: ob.Time,
                    SizePips: Math.Round((ob.High - ob.Low) / PipSize, 1),
                    Mitigated: mitigated));
            }
        }

        return blocks;
    }
}
