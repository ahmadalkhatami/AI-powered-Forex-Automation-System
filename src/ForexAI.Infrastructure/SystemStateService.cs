using System.Text.Json;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;

namespace ForexAI.Infrastructure;

/// <summary>
/// Singleton: state runtime sistem (halt flag, spread limit, circuit breaker config).
/// Persist ke disk supaya bertahan setelah backend restart.
///
/// <para><b>Halt:</b> kill switch user-triggered — semua execute path harus check IsHalted dulu.</para>
/// <para><b>MaxSpreadPips:</b> hard reject order kalau spread broker melebar terlalu lebar.</para>
/// <para><b>MaxConsecutiveLosses:</b> threshold circuit breaker — di-handle di risk evaluator.</para>
/// </summary>
public class SystemStateService : ISystemStateService
{
    public bool IsHalted { get; private set; }
    public string? HaltReason { get; private set; }
    public DateTimeOffset? HaltedAt { get; private set; }

    public decimal MaxSpreadPips { get; private set; } = 2.5m;
    public int MaxConsecutiveLosses { get; private set; } = 3;
    public int MaxHoldingMinutes { get; private set; } = 360;  // 6 jam (M15: 24 bars)

    // Post-loss cooldown: setelah trade rugi, block same-direction selama 30 menit
    public SignalDirection? LastLossDirection { get; private set; }
    public DateTimeOffset? LastLossAt { get; private set; }
    public int CooldownMinutes { get; private set; } = 30;

    private readonly string _persistPath;
    private readonly object _lock = new();

    public SystemStateService()
    {
        Directory.CreateDirectory(ProjectPaths.ImplementationArtifactsDir);
        _persistPath = Path.Combine(ProjectPaths.ImplementationArtifactsDir, "system-state.json");
        Load();
    }

    public void Halt(string reason)
    {
        lock (_lock)
        {
            IsHalted   = true;
            HaltReason = reason;
            HaltedAt   = DateTimeOffset.UtcNow;
            Save_NoLock();
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            IsHalted   = false;
            HaltReason = null;
            HaltedAt   = null;
            Save_NoLock();
        }
    }

    public void RegisterLoss(SignalDirection direction)
    {
        if (direction == SignalDirection.HOLD) return;
        lock (_lock)
        {
            LastLossDirection = direction;
            LastLossAt        = DateTimeOffset.UtcNow;
            Save_NoLock();
        }
    }

    public bool IsInCooldown(SignalDirection direction, out int minutesRemaining)
    {
        minutesRemaining = 0;
        if (CooldownMinutes <= 0 || LastLossAt is null || LastLossDirection is null) return false;
        if (LastLossDirection.Value != direction) return false;

        var elapsed = (DateTimeOffset.UtcNow - LastLossAt.Value).TotalMinutes;
        if (elapsed >= CooldownMinutes) return false;
        minutesRemaining = (int)Math.Ceiling(CooldownMinutes - elapsed);
        return true;
    }

    private record Snapshot(
        bool IsHalted,
        string? HaltReason,
        DateTimeOffset? HaltedAt,
        decimal MaxSpreadPips,
        int MaxConsecutiveLosses,
        int MaxHoldingMinutes = 360,
        SignalDirection? LastLossDirection = null,
        DateTimeOffset? LastLossAt = null,
        int CooldownMinutes = 30);

    private void Save_NoLock()
    {
        try
        {
            var snap = new Snapshot(
                IsHalted, HaltReason, HaltedAt,
                MaxSpreadPips, MaxConsecutiveLosses, MaxHoldingMinutes,
                LastLossDirection, LastLossAt, CooldownMinutes);
            var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true });
            var tmp  = _persistPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _persistPath, overwrite: true);
        }
        catch { /* best effort */ }
    }

    private void Load()
    {
        if (!File.Exists(_persistPath)) return;
        try
        {
            var snap = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_persistPath));
            if (snap is null) return;
            lock (_lock)
            {
                IsHalted             = snap.IsHalted;
                HaltReason           = snap.HaltReason;
                HaltedAt             = snap.HaltedAt;
                MaxSpreadPips        = snap.MaxSpreadPips > 0 ? snap.MaxSpreadPips : 2.5m;
                MaxConsecutiveLosses = snap.MaxConsecutiveLosses > 0 ? snap.MaxConsecutiveLosses : 3;
                MaxHoldingMinutes    = snap.MaxHoldingMinutes > 0 ? snap.MaxHoldingMinutes : 360;
                LastLossDirection    = snap.LastLossDirection;
                LastLossAt           = snap.LastLossAt;
                CooldownMinutes      = snap.CooldownMinutes > 0 ? snap.CooldownMinutes : 30;
            }
        }
        catch { /* corrupt — ignore */ }
    }
}
