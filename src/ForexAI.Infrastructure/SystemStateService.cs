using System.Text.Json;
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

    private record Snapshot(
        bool IsHalted,
        string? HaltReason,
        DateTimeOffset? HaltedAt,
        decimal MaxSpreadPips,
        int MaxConsecutiveLosses,
        int MaxHoldingMinutes = 360);

    private void Save_NoLock()
    {
        try
        {
            var snap = new Snapshot(IsHalted, HaltReason, HaltedAt, MaxSpreadPips, MaxConsecutiveLosses, MaxHoldingMinutes);
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
            }
        }
        catch { /* corrupt — ignore */ }
    }
}
