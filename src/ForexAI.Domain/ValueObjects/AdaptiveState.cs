namespace ForexAI.Domain.ValueObjects;

/// <summary>
/// Current state Adaptive Learning Engine — per-regime overrides, session penalties,
/// cooldown adaptations, pattern disables. Plus audit history (last N changes).
///
/// <para>Semua field optional / nullable: kalau belum pernah di-tune, fallback ke
/// baseline di SystemStateService (manual config).</para>
/// </summary>
public record AdaptiveState(
    // Master kill switch — true = Adaptive Engine TIDAK auto-fire apapun
    bool MasterDisabled,

    // Per-Tier-1-action kill switches
    bool RegimeThresholdActionDisabled,
    bool SessionPenaltyActionDisabled,
    bool CooldownActionDisabled,
    bool PatternActionDisabled,

    // Per-regime confidence threshold override (null = pakai baseline)
    // Key: regime name (Trending/Ranging/Transitional/Volatile)
    IReadOnlyDictionary<string, decimal> RegimeThresholdOverride,

    // Per-session penalty (additive subtraction dari confidence — 0.05 = -5%)
    // Key: session name (London/NewYork/Tokyo/Sydney/Overlap)
    IReadOnlyDictionary<string, decimal> SessionPenalty,

    // Session skip status — kalau ada entry, signal di-block untuk session itu sampai ExpiresAt
    IReadOnlyDictionary<string, DateTimeOffset> SessionSkipUntil,

    // Cooldown length override per direction (menit) — null = pakai baseline 30
    IReadOnlyDictionary<string, int> CooldownOverride,    // key: "BUY"/"SELL"

    // Pattern disable status — kalau ada entry, pattern boost = 0 untuk pattern itu
    IReadOnlyDictionary<string, DateTimeOffset> PatternDisableUntil,

    // Audit history (last 50 entries, newest first)
    IReadOnlyList<AdaptiveAuditEntry> AuditHistory)
{
    public static AdaptiveState Empty() => new(
        MasterDisabled: false,
        RegimeThresholdActionDisabled: false,
        SessionPenaltyActionDisabled: false,
        CooldownActionDisabled: false,
        PatternActionDisabled: false,
        RegimeThresholdOverride: new Dictionary<string, decimal>(),
        SessionPenalty: new Dictionary<string, decimal>(),
        SessionSkipUntil: new Dictionary<string, DateTimeOffset>(),
        CooldownOverride: new Dictionary<string, int>(),
        PatternDisableUntil: new Dictionary<string, DateTimeOffset>(),
        AuditHistory: Array.Empty<AdaptiveAuditEntry>());
}

/// <summary>
/// Audit entry — record of single Adaptive Engine adjustment, with evidence + reason.
/// Persisted ke AdaptiveState.AuditHistory + dipakai snapshot reason.json.
/// </summary>
public record AdaptiveAuditEntry(
    DateTimeOffset Timestamp,
    string Action,              // "RegimeThreshold" / "SessionPenalty" / "SessionSkip" / "CooldownAdapt" / "PatternDisable" / "Revert"
    string Bucket,              // e.g. "Ranging" / "Tokyo" / "Bullish Pin Bar"
    string Parameter,           // human-readable parameter name
    string FromValue,
    string ToValue,
    string Reason,              // e.g. "WR 33% below 40% threshold, sample n=24, p<0.05"
    int    SampleSize,
    decimal? WilsonLower,
    decimal? WilsonUpper,
    decimal? ExpectancyR,
    string  SnapshotId);        // FK ke snapshot directory
