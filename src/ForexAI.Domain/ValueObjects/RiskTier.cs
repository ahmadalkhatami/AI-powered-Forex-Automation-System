namespace ForexAI.Domain.ValueObjects;

/// <summary>
/// Risk tier yang menentukan parameter risk per trade dan daily cap berdasarkan equity.
/// Modal kecil ($50) butuh % risk lebih besar agar 0.01 lot (lot minimum MIFX) tetap viable;
/// modal besar (&gt;$500) bisa kembali ke 1% standar dengan daily cap yang lebih ketat.
///
/// Daily cap selalu &gt; risk per trade × max daily trades agar consecutive winners tidak ter-block.
/// </summary>
public record RiskTier(
    string  Name,                // "starter" | "growth" | "stable" | "scaled"
    decimal MinEquity,           // batas bawah tier ini (USD)
    decimal RiskPerTradePct,     // 0.020 = 2% equity per trade
    decimal DailyCapPct,         // 0.060 = 6% equity total exposure per hari
    int     MaxDailyTrades)      // jumlah trade max per hari (UTC day)
{
    public static readonly RiskTier Starter = new("starter",   0m, 0.020m, 0.060m, 3);
    public static readonly RiskTier Growth  = new("growth", 100m, 0.015m, 0.060m, 4);
    public static readonly RiskTier Stable  = new("stable", 200m, 0.010m, 0.050m, 5);
    public static readonly RiskTier Scaled  = new("scaled", 500m, 0.010m, 0.040m, 5);

    public static RiskTier FromEquity(decimal equity)
    {
        if (equity < 100m) return Starter;
        if (equity < 200m) return Growth;
        if (equity < 500m) return Stable;
        return Scaled;
    }
}
