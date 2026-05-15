using ForexAI.Domain.Enums;

namespace ForexAI.Domain.Interfaces;

/// <summary>
/// Runtime state sistem: kill switch + safety thresholds.
/// Implementasi di Infrastructure dengan persistence ke disk.
/// </summary>
public interface ISystemStateService
{
    bool             IsHalted             { get; }
    string?          HaltReason           { get; }
    DateTimeOffset?  HaltedAt             { get; }
    decimal          MaxSpreadPips        { get; }
    int              MaxConsecutiveLosses { get; }
    int              MaxHoldingMinutes    { get; }    // 0 = disabled

    // Post-loss cooldown: setelah LOSS, block same-direction selama N menit
    // untuk mencegah revenge-trade di kondisi market yang sama.
    SignalDirection? LastLossDirection   { get; }
    DateTimeOffset?  LastLossAt          { get; }
    int              CooldownMinutes     { get; }    // 0 = disabled

    void Halt(string reason);
    void Resume();
    void RegisterLoss(SignalDirection direction);
    bool IsInCooldown(SignalDirection direction, out int minutesRemaining);
}
