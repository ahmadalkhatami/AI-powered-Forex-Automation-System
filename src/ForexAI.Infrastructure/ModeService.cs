using System.Text.Json;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;

namespace ForexAI.Infrastructure;

public class ModeService : IModeService
{
    public TradeMode CurrentMode { get; private set; } = TradeMode.Demo;
    public DateTimeOffset? LastReportedAt { get; private set; }

    public event EventHandler<ModeChangedEventArgs>? ModeChanged;

    private readonly string _persistPath;
    private readonly object _lock = new();

    public ModeService()
    {
        // Persist di _bmad-output/ root (BUKAN di implementation-artifacts-* karena
        // itu lokasinya bergantung mode — chicken-and-egg).
        Directory.CreateDirectory(ProjectPaths.ArtifactsDir);
        _persistPath = Path.Combine(ProjectPaths.ArtifactsDir, "mode-state.json");
        Load();
    }

    public void ReportFromEa(string? accountMode)
    {
        // EA kirim "REAL" untuk real account, selain itu (DEMO, CONTEST, null) → Demo
        var newMode = string.Equals(accountMode, "REAL", StringComparison.OrdinalIgnoreCase)
            ? TradeMode.Real
            : TradeMode.Demo;

        ModeChangedEventArgs? evt = null;
        lock (_lock)
        {
            LastReportedAt = DateTimeOffset.UtcNow;
            if (CurrentMode != newMode)
            {
                evt = new ModeChangedEventArgs(CurrentMode, newMode, LastReportedAt.Value);
                CurrentMode = newMode;
                Save_NoLock();
            }
        }

        if (evt is not null) ModeChanged?.Invoke(this, evt);
    }

    private record Snapshot(TradeMode CurrentMode, DateTimeOffset? LastReportedAt);

    private void Save_NoLock()
    {
        try
        {
            var snap = new Snapshot(CurrentMode, LastReportedAt);
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
                CurrentMode    = snap.CurrentMode;
                LastReportedAt = snap.LastReportedAt;
            }
        }
        catch { /* corrupt — ignore */ }
    }
}
