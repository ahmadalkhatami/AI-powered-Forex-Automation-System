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

    void Halt(string reason);
    void Resume();
}
