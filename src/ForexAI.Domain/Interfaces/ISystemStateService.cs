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

    // Weekly drawdown cap — halt kalau realized loss rolling 7 hari > N% equity (default 5%)
    decimal          MaxWeeklyDrawdownPct { get; }

    void Halt(string reason);
    void Resume();
    void RegisterLoss(SignalDirection direction);
    bool IsInCooldown(SignalDirection direction, out int minutesRemaining);

    void UpdateConfig(
        decimal? maxSpreadPips = null,
        int? maxConsecutiveLosses = null,
        int? maxHoldingMinutes = null,
        int? cooldownMinutes = null,
        decimal? nanoMaxDailyLossUsd = null,
        decimal? nanoEquityFloorUsd = null,
        decimal? maxWeeklyDrawdownPct = null);
}
