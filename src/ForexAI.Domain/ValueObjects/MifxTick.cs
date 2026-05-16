namespace ForexAI.Domain.ValueObjects;

public record MifxTick(
    string Pair,
    decimal Bid,
    decimal Ask,
    DateTimeOffset Time,
    decimal? AccountBalance = null,   // Balance akun broker (dari MT5)
    decimal? AccountEquity  = null,   // Equity akun broker termasuk floating P&L
    // ── Indikator teknikal (dikirim EA v1.15+) ──────────────────────
    decimal? MA20_M15    = null,
    decimal? MA50_M15    = null,
    decimal? MA20_H1     = null,
    decimal? MA50_H1     = null,
    decimal? RSI14       = null,
    int?     RSIDir      = null,   // 1=rising, 0=falling
    decimal? ATR14       = null,   // ATR(14) M15 dalam satuan harga (EA v1.16+)
    decimal? ADX14       = null,   // ADX(14) M15 trend strength 0-100 (EA v1.17+)
    decimal? Support     = null,
    decimal? Resistance  = null,
    // Mode account MT5 (EA v1.22+): "REAL" | "DEMO" | "CONTEST" | null (EA versi lama)
    string?  AccountMode = null
)
{
    public decimal Mid    => Math.Round((Bid + Ask) / 2m, 5);
    public decimal Spread => Math.Round((Ask - Bid) * 10000m, 1); // in pips

    /// <summary>True jika tick membawa data indikator lengkap (EA v1.16+)</summary>
    public bool HasIndicators =>
        MA20_M15.HasValue && MA50_M15.HasValue &&
        MA20_H1.HasValue  && MA50_H1.HasValue  &&
        RSI14.HasValue    && ATR14.HasValue;
}
