using System.Text.Json;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure;

/// <summary>
/// File-backed implementation of <see cref="IAdaptiveStateService"/>.
/// State persisted to `data/{mode}/adaptive-state.json`. Snapshots written to
/// `data/{mode}/adaptive-snapshots/{timestamp}-{action}/`.
///
/// <para>Thread-safe via lock. Mode-aware: reload on mode change, reset semua override
/// supaya demo learning tidak bocor ke real mode.</para>
/// </summary>
public class AdaptiveStateService : IAdaptiveStateService
{
    private const int MaxSnapshots = 50;
    private const int MaxAuditEntries = 50;

    private readonly IModeService _mode;
    private readonly ISystemStateService _systemState;  // untuk snapshot config_before/after
    private readonly object _lock = new();
    private AdaptiveState _state = AdaptiveState.Empty();

    public AdaptiveStateService(IModeService mode, ISystemStateService systemState)
    {
        _mode = mode;
        _systemState = systemState;
        _mode.ModeChanged += OnModeChanged;
        Load();
    }

    public AdaptiveState Current
    {
        get { lock (_lock) return _state; }
    }

    public string Apply(AdaptiveStateUpdate update, AdaptiveAuditEntry auditEntry)
    {
        lock (_lock)
        {
            var before = _state;
            var beforeSystem = CaptureSystemSnapshot();

            // Apply partial update
            var newRegime = update.RegimeThresholdSet is not null
                ? MergeDict(before.RegimeThresholdOverride, update.RegimeThresholdSet)
                : before.RegimeThresholdOverride;
            var newSessPen = update.SessionPenaltySet is not null
                ? MergeDict(before.SessionPenalty, update.SessionPenaltySet)
                : before.SessionPenalty;
            var newSessSkip = update.SessionSkipUntilSet is not null
                ? MergeDict(before.SessionSkipUntil, update.SessionSkipUntilSet)
                : before.SessionSkipUntil;
            var newCooldown = update.CooldownOverrideSet is not null
                ? MergeDict(before.CooldownOverride, update.CooldownOverrideSet)
                : before.CooldownOverride;
            var newPatternDis = update.PatternDisableUntilSet is not null
                ? MergeDict(before.PatternDisableUntil, update.PatternDisableUntilSet)
                : before.PatternDisableUntil;

            // Generate snapshot id sebelum prepend audit (audit entry sendiri reference snapshot id)
            string snapshotId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Sanitize(auditEntry.Action)}";
            var auditWithSnapshot = auditEntry with { SnapshotId = snapshotId };

            // Prepend audit entry, cap to MaxAuditEntries
            var newAudit = new List<AdaptiveAuditEntry>(before.AuditHistory.Count + 1) { auditWithSnapshot };
            newAudit.AddRange(before.AuditHistory.Take(MaxAuditEntries - 1));

            var after = before with
            {
                RegimeThresholdOverride = newRegime,
                SessionPenalty          = newSessPen,
                SessionSkipUntil        = newSessSkip,
                CooldownOverride        = newCooldown,
                PatternDisableUntil     = newPatternDis,
                AuditHistory            = newAudit
            };
            _state = after;
            Save_NoLock();

            WriteSnapshot(snapshotId, beforeSystem, after, auditWithSnapshot);
            PruneSnapshots();

            return snapshotId;
        }
    }

    public IReadOnlyList<string> ListSnapshots()
    {
        var dir = SnapshotsDir;
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetDirectories(dir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .Take(MaxSnapshots)
            .Cast<string>()
            .ToList();
    }

    public AdaptiveSnapshotBundle? ReadSnapshot(string snapshotId)
    {
        var dir = Path.Combine(SnapshotsDir, snapshotId);
        if (!Directory.Exists(dir)) return null;
        try
        {
            string before = File.ReadAllText(Path.Combine(dir, "config_before.json"));
            string after  = File.ReadAllText(Path.Combine(dir, "config_after.json"));
            string reason = File.ReadAllText(Path.Combine(dir, "reason.json"));
            return new AdaptiveSnapshotBundle(snapshotId, before, after, reason);
        }
        catch { return null; }
    }

    public bool Rollback(string snapshotId, string requestedBy)
    {
        var bundle = ReadSnapshot(snapshotId);
        if (bundle is null) return false;

        lock (_lock)
        {
            try
            {
                using var doc = JsonDocument.Parse(bundle.ConfigBeforeJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("adaptiveState", out var adaptiveEl)) return false;

                var restored = JsonSerializer.Deserialize<AdaptiveState>(adaptiveEl.GetRawText(), JsonOpts);
                if (restored is null) return false;

                // Apply restored state, lalu tulis audit "Revert" sebagai action.
                var revertAudit = new AdaptiveAuditEntry(
                    Timestamp:    DateTimeOffset.UtcNow,
                    Action:       "Revert",
                    Bucket:       snapshotId,
                    Parameter:    "AllAdaptiveState",
                    FromValue:    "current",
                    ToValue:      "snapshot-before",
                    Reason:       $"Manual rollback requested by {requestedBy} to snapshot {snapshotId}",
                    SampleSize:   0,
                    WilsonLower:  null,
                    WilsonUpper:  null,
                    ExpectancyR:  null,
                    SnapshotId:   "");  // di-set di Apply

                // Apply by Apply() supaya snapshot baru juga tertulis.
                _state = restored;  // langsung set state ke restored
                Save_NoLock();

                // Write Revert snapshot (current=restored, before=restored, after=restored — minimal audit trail)
                string newSnapshotId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-Revert";
                var auditWithId = revertAudit with { SnapshotId = newSnapshotId };
                var newAudit = new List<AdaptiveAuditEntry>(restored.AuditHistory.Count + 1) { auditWithId };
                newAudit.AddRange(restored.AuditHistory.Take(MaxAuditEntries - 1));
                _state = restored with { AuditHistory = newAudit };
                Save_NoLock();

                WriteSnapshot(newSnapshotId, CaptureSystemSnapshot(), _state, auditWithId);
                PruneSnapshots();

                return true;
            }
            catch { return false; }
        }
    }

    public void SetMasterDisabled(bool disabled)
    {
        lock (_lock)
        {
            _state = _state with { MasterDisabled = disabled };
            Save_NoLock();
        }
    }

    public void SetActionDisabled(string actionName, bool disabled)
    {
        lock (_lock)
        {
            _state = actionName switch
            {
                "RegimeThreshold" => _state with { RegimeThresholdActionDisabled = disabled },
                "SessionPenalty"  => _state with { SessionPenaltyActionDisabled  = disabled },
                "Cooldown"        => _state with { CooldownActionDisabled        = disabled },
                "Pattern"         => _state with { PatternActionDisabled         = disabled },
                _ => _state
            };
            Save_NoLock();
        }
    }

    // ── Internal helpers ─────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private string PersistPath
    {
        get
        {
            var dir = ProjectPaths.GetImplementationArtifactsDir(_mode.CurrentMode);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "adaptive-state.json");
        }
    }

    private string SnapshotsDir
    {
        get
        {
            var dir = Path.Combine(
                ProjectPaths.GetImplementationArtifactsDir(_mode.CurrentMode),
                "adaptive-snapshots");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private object CaptureSystemSnapshot()
    {
        return new
        {
            timestamp = DateTimeOffset.UtcNow,
            systemState = new
            {
                _systemState.IsHalted,
                _systemState.HaltReason,
                _systemState.MaxSpreadPips,
                _systemState.MaxConsecutiveLosses,
                _systemState.MaxHoldingMinutes,
                _systemState.CooldownMinutes,
                _systemState.NanoMaxDailyLossUsd,
                _systemState.NanoEquityFloorUsd,
                _systemState.MaxWeeklyDrawdownPct,
                _systemState.MaxTradesPerDay,
                _systemState.AutoApproveMinConfidence
            },
            adaptiveState = _state
        };
    }

    private void WriteSnapshot(string snapshotId, object beforeBundle, AdaptiveState after, AdaptiveAuditEntry audit)
    {
        var dir = Path.Combine(SnapshotsDir, snapshotId);
        Directory.CreateDirectory(dir);
        try
        {
            // config_before.json — full system+adaptive state SEBELUM change (capture earlier)
            File.WriteAllText(Path.Combine(dir, "config_before.json"),
                JsonSerializer.Serialize(beforeBundle, JsonOpts));

            // config_after.json — full state SETELAH change
            var afterBundle = new
            {
                timestamp = DateTimeOffset.UtcNow,
                systemState = new
                {
                    _systemState.IsHalted,
                    _systemState.HaltReason,
                    _systemState.MaxSpreadPips,
                    _systemState.MaxConsecutiveLosses,
                    _systemState.MaxHoldingMinutes,
                    _systemState.CooldownMinutes,
                    _systemState.NanoMaxDailyLossUsd,
                    _systemState.NanoEquityFloorUsd,
                    _systemState.MaxWeeklyDrawdownPct,
                    _systemState.MaxTradesPerDay,
                    _systemState.AutoApproveMinConfidence
                },
                adaptiveState = after
            };
            File.WriteAllText(Path.Combine(dir, "config_after.json"),
                JsonSerializer.Serialize(afterBundle, JsonOpts));

            // reason.json — evidence + decision rationale (per § 9.1 roadmap)
            File.WriteAllText(Path.Combine(dir, "reason.json"),
                JsonSerializer.Serialize(audit, JsonOpts));
        }
        catch { /* best effort — audit log absent acceptable */ }
    }

    private void PruneSnapshots()
    {
        try
        {
            var snapshots = Directory.GetDirectories(SnapshotsDir)
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                .Skip(MaxSnapshots)
                .ToList();
            foreach (var dir in snapshots) Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }

    private static IReadOnlyDictionary<TKey, TVal> MergeDict<TKey, TVal>(
        IReadOnlyDictionary<TKey, TVal> existing, Dictionary<TKey, TVal> updates) where TKey : notnull
    {
        var merged = new Dictionary<TKey, TVal>(existing);
        foreach (var (k, v) in updates) merged[k] = v;
        return merged;
    }

    private static string Sanitize(string s) =>
        new(s.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());

    private void OnModeChanged(object? sender, ModeChangedEventArgs e)
    {
        lock (_lock)
        {
            _state = AdaptiveState.Empty();
        }
        Load();
    }

    private void Save_NoLock()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, JsonOpts);
            var path = PersistPath;
            var tmp = path + ".tmp";
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
            var loaded = JsonSerializer.Deserialize<AdaptiveState>(File.ReadAllText(path), JsonOpts);
            if (loaded is null) return;
            lock (_lock) _state = loaded;
        }
        catch { /* corrupt — ignore, start fresh */ }
    }
}
