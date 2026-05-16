using ForexAI.Domain.Enums;

namespace ForexAI.Domain.Interfaces;

public record ModeChangedEventArgs(TradeMode From, TradeMode To, DateTimeOffset At);

/// <summary>
/// Singleton: track mode account aktif (Demo/Real) dari EA. Backend pakai untuk
/// routing storage path + adaptive risk tier. Mode change otomatis di-detect dari
/// EA tick payload — zero manual toggle.
/// </summary>
public interface IModeService
{
    TradeMode CurrentMode { get; }
    DateTimeOffset? LastReportedAt { get; }
    event EventHandler<ModeChangedEventArgs>? ModeChanged;

    /// <summary>
    /// Dipanggil oleh MifxBridgeController saat EA tick masuk. Argument bisa
    /// "REAL", "DEMO", atau "CONTEST" — selain "REAL" di-treat sebagai Demo.
    /// Kalau berubah dari mode current, fire ModeChanged event.
    /// </summary>
    void ReportFromEa(string? accountMode);
}
