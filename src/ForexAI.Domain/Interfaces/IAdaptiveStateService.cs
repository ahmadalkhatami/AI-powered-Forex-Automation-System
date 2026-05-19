using ForexAI.Domain.ValueObjects;

namespace ForexAI.Domain.Interfaces;

/// <summary>
/// Service yang manage state Adaptive Learning Engine — current overrides + audit history.
///
/// <para>File-backed (`data/{mode}/adaptive-state.json`), mode-aware (demo/real terpisah).
/// Setiap mutation otomatis tulis snapshot ke `data/{mode}/adaptive-snapshots/` untuk
/// rollback + audit trail.</para>
/// </summary>
public interface IAdaptiveStateService
{
    /// <summary>Snapshot lengkap state Adaptive saat ini.</summary>
    AdaptiveState Current { get; }

    /// <summary>
    /// Apply Tier-1 adjustment dengan write-through audit + snapshot.
    /// Caller MUST provide reason + evidence — di-record di AuditHistory.
    /// </summary>
    /// <returns>Snapshot ID yang baru ditulis, untuk rollback reference.</returns>
    string Apply(AdaptiveStateUpdate update, AdaptiveAuditEntry auditEntry);

    /// <summary>List of snapshot IDs (newest first, max 50).</summary>
    IReadOnlyList<string> ListSnapshots();

    /// <summary>Read snapshot files (config_before.json + config_after.json + reason.json).</summary>
    AdaptiveSnapshotBundle? ReadSnapshot(string snapshotId);

    /// <summary>
    /// Rollback ke before-state dari snapshot tertentu. Tulis new snapshot dengan
    /// action="Revert" supaya rollback itu sendiri ter-audit.
    /// </summary>
    bool Rollback(string snapshotId, string requestedBy);

    /// <summary>Master kill switch — toggle ON/OFF entire Adaptive Engine.</summary>
    void SetMasterDisabled(bool disabled);

    /// <summary>Per-action kill switch.</summary>
    void SetActionDisabled(string actionName, bool disabled);
}

/// <summary>
/// Partial update — hanya field non-null yang di-apply. Pattern serupa SystemStateService.UpdateConfig.
/// </summary>
public record AdaptiveStateUpdate(
    Dictionary<string, decimal>? RegimeThresholdSet = null,
    Dictionary<string, decimal>? SessionPenaltySet = null,
    Dictionary<string, DateTimeOffset>? SessionSkipUntilSet = null,
    Dictionary<string, int>? CooldownOverrideSet = null,
    Dictionary<string, DateTimeOffset>? PatternDisableUntilSet = null);

/// <summary>Read result untuk snapshot bundle — 3 file JSON yang ditulis saat Adaptive fire.</summary>
public record AdaptiveSnapshotBundle(
    string SnapshotId,
    string ConfigBeforeJson,
    string ConfigAfterJson,
    string ReasonJson);
