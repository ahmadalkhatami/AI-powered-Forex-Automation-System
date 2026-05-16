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
/// <para><b>Mode-aware:</b> file path otomatis pindah ikut mode (demo vs real). Saat mode berubah,
/// state runtime ter-reload dari path baru.</para>
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

    // Hard $ caps untuk full-auto Nano safety (modal $30-60).
    // Default: stop trading kalau hari ini sudah loss > $5, atau equity drop ke ≤ $20.
    public decimal NanoMaxDailyLossUsd { get; private set; } = 5m;
    public decimal NanoEquityFloorUsd  { get; private set; } = 20m;

    private readonly IModeService _mode;
    private readonly object _lock = new();

    private string PersistPath
    {
        get
        {
            var dir = ProjectPaths.GetImplementationArtifactsDir(_mode.CurrentMode);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "system-state.json");
        }
    }

    public SystemStateService(IModeService mode)
    {
        _mode = mode;
        _mode.ModeChanged += OnModeChanged;
        Load();
    }

    private void OnModeChanged(object? sender, ModeChangedEventArgs e)
    {
        // Reset state ke default + reload dari file mode baru.
        // Cooldown post-loss tidak boleh terbawa antar mode (demo loss ≠ block real trade).
        lock (_lock)
        {
            IsHalted = false; HaltReason = null; HaltedAt = null;
            LastLossDirection = null; LastLossAt = null;
            MaxSpreadPips = 2.5m; MaxConsecutiveLosses = 3; MaxHoldingMinutes = 360; CooldownMinutes = 30;
        }
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
        int CooldownMinutes = 30,
        decimal NanoMaxDailyLossUsd = 5m,
        decimal NanoEquityFloorUsd  = 20m);

    private void Save_NoLock()
    {
        try
        {
            var snap = new Snapshot(
                IsHalted, HaltReason, HaltedAt,
                MaxSpreadPips, MaxConsecutiveLosses, MaxHoldingMinutes,
                LastLossDirection, LastLossAt, CooldownMinutes,
                NanoMaxDailyLossUsd, NanoEquityFloorUsd);
            var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true });
            var path = PersistPath;
            var tmp  = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* best effort */ }
    }

    private void Load()
    {
        var path = PersistPath;
        if (!File.Exists(path)) return;
        try
        {
            var snap = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path));
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
                NanoMaxDailyLossUsd  = snap.NanoMaxDailyLossUsd > 0m ? snap.NanoMaxDailyLossUsd : 5m;
                NanoEquityFloorUsd   = snap.NanoEquityFloorUsd  > 0m ? snap.NanoEquityFloorUsd  : 20m;
            }
        }
        catch { /* corrupt — ignore */ }
    }
}
