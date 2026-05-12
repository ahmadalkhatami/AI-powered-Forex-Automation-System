namespace ForexAI.Domain.ValueObjects;

/// <summary>
/// Snapshot total risk yang sudah dipakai hari ini (UTC day).
/// Reset otomatis pada UTC midnight (07:00 WIB).
///
/// <para>UsedUsd: penjumlahan RiskAmount semua TradePosition dengan OpenedAt di hari ini.
/// Termasuk trade yang sudah closed (WIN/LOSS) — sekali equity sudah "dipertaruhkan", terhitung.</para>
/// </summary>
public record DailyRiskUsage(
    decimal         UsedUsd,        // total risk_amount trades opened today (USD)
    int             TradeCount,     // jumlah trade opened today
    DateTimeOffset  AsOfUtc)        // saat snapshot dihitung
{
    public static DailyRiskUsage Empty(DateTimeOffset now) => new(0m, 0, now);

    /// <summary>Persentase risk yang sudah dipakai hari ini relatif terhadap equity.</summary>
    public decimal UsedPct(decimal equity) =>
        equity > 0m ? Math.Round(UsedUsd / equity, 4) : 0m;
}
