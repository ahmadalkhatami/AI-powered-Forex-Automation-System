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
    decimal? Support     = null,
    decimal? Resistance  = null
)
{
    public decimal Mid    => Math.Round((Bid + Ask) / 2m, 5);
    public decimal Spread => Math.Round((Ask - Bid) * 10000m, 1); // in pips

    /// <summary>True jika tick membawa data indikator lengkap (EA v1.15+)</summary>
    public bool HasIndicators =>
        MA20_M15.HasValue && MA50_M15.HasValue &&
        MA20_H1.HasValue  && MA50_H1.HasValue  &&
        RSI14.HasValue;
}
