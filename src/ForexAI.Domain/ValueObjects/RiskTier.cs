using ForexAI.Domain.Enums;

namespace ForexAI.Domain.ValueObjects;

/// <summary>
/// Risk tier yang menentukan parameter risk per trade dan daily cap berdasarkan equity.
/// Modal kecil ($50) butuh % risk lebih besar agar 0.01 lot (lot minimum MIFX) tetap viable;
/// modal besar (&gt;$500) bisa kembali ke 1% standar dengan daily cap yang lebih ketat.
///
/// <para><b>Nano tier:</b> hanya aktif di REAL mode + modal &lt; $100. Math: 0.01 lot × 15p SL = $1.50
/// minimum risk = 5% dari $30 modal — unavoidable karena broker min lot. Diimbangi dengan
/// quality threshold yang sangat ketat di analyzer (confluence ≥ 80, conf ≥ 0.75) + halt cepat.</para>
///
/// Daily cap selalu &gt; risk per trade × max daily trades agar consecutive winners tidak ter-block.
/// </summary>
public record RiskTier(
    string  Name,                // "nano" | "starter" | "growth" | "stable" | "scaled"
    decimal MinEquity,           // batas bawah tier ini (USD)
    decimal RiskPerTradePct,     // 0.020 = 2% equity per trade
    decimal DailyCapPct,         // 0.060 = 6% equity total exposure per hari
    int     MaxDailyTrades)      // jumlah trade max per hari (UTC day)
{
    // Cap = RiskPerTrade × MaxDailyTrades — agar trade berturut-turut lolos sebelum cap memblok.
    // ⚠️ Nano: 7 trades × 5% = 35% daily cap = HIGH variance acknowledge.
    // Mitigasi: halt @ 2 conseq loss = practical max -10% sebelum stop, plus quality vetos ekstrem.
    public static readonly RiskTier Nano    = new("nano",    0m, 0.050m, 0.350m, 7);  // REAL + < $100
    public static readonly RiskTier Starter = new("starter", 0m, 0.020m, 0.140m, 7);
    public static readonly RiskTier Growth  = new("growth", 100m, 0.015m, 0.110m, 7);
    public static readonly RiskTier Stable  = new("stable", 200m, 0.010m, 0.070m, 7);
    public static readonly RiskTier Scaled  = new("scaled", 500m, 0.010m, 0.070m, 7);

    /// <summary>
    /// Pilih tier sesuai equity + mode. Real mode dengan modal &lt; $100 → Nano (5% per trade).
    /// Demo mode pakai tier standar walau modal kecil — demo bisa lebih agresif untuk testing.
    /// </summary>
    public static RiskTier FromEquity(decimal equity, TradeMode mode = TradeMode.Demo)
    {
        if (mode == TradeMode.Real && equity < 100m) return Nano;
        if (equity < 100m) return Starter;
        if (equity < 200m) return Growth;
        if (equity < 500m) return Stable;
        return Scaled;
    }
}
