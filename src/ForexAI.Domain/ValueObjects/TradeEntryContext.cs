namespace ForexAI.Domain.ValueObjects;

/// <summary>
/// Adaptive learning enrich context — captured saat signal generation untuk analytics
/// per-bucket (regime / session / pattern / zone) dan tracking outcome (MFE / MAE / exitReason).
///
/// Semua field optional supaya caller lama tetap berfungsi tanpa coupling.
/// </summary>
public record TradeEntryContext(
    string? Session = null,
    string? Regime = null,
    string? PatternName = null,
    string? PatternBias = null,
    decimal? PatternReliability = null,
    bool? SweepDetected = null,
    string? Zone = null,
    decimal? Confidence = null);
