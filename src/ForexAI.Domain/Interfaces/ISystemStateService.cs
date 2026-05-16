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

    // Hard $ caps untuk full-auto safety di Nano mode (tiny modal).
    // 0 = disabled (pakai % cap dari tier saja). Default Nano: -$5 daily, floor $20.
    decimal          NanoMaxDailyLossUsd { get; }   // auto-halt kalau today's PnL <= -nilai ini
    decimal          NanoEquityFloorUsd  { get; }   // auto-halt PERMANENT kalau equity <= nilai ini

    void Halt(string reason);
    void Resume();
    void RegisterLoss(SignalDirection direction);
    bool IsInCooldown(SignalDirection direction, out int minutesRemaining);
}
