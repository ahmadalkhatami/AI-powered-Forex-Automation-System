using System.Text.Json;
using ForexAI.Domain.Interfaces;

namespace ForexAI.Infrastructure;

public record AuditEvent(
    DateTimeOffset Timestamp,
    string Type,        // "signal", "risk", "execute", "close", "halt", "resume", "block"
    string Summary,     // 1-line human readable
    object? Payload     // full context (signal, risk validation, etc)
);

/// <summary>
/// Append-only audit log untuk semua decision events (signal, risk, execute, close, halt).
/// Disimpan di JSON Lines (1 event per baris) untuk efisiensi parse + rotate.
///
/// <para>Thread-safe, fire-and-forget (best-effort write, swallow IO errors).
/// Truncate ke max 10000 events pada startup untuk hindari file bloat.</para>
/// </summary>
public class AuditLogger
{
    private const int MaxEvents = 10_000;
    private readonly IModeService _mode;
    private readonly object _lock = new();

    private string _path
    {
        get
        {
            var dir = ProjectPaths.GetImplementationArtifactsDir(_mode.CurrentMode);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "audit-log.jsonl");
        }
    }

    public AuditLogger(IModeService mode)
    {
        _mode = mode;
        _mode.ModeChanged += (_, _) => TruncateIfTooLarge();
        TruncateIfTooLarge();
    }

    public void Log(string type, string summary, object? payload = null)
    {
        var evt = new AuditEvent(DateTimeOffset.UtcNow, type, summary, payload);
        var line = JsonSerializer.Serialize(evt) + Environment.NewLine;
        try
        {
            lock (_lock) File.AppendAllText(_path, line);
        }
        catch { /* best effort */ }
    }

    /// <summary>Read last N events (most recent first).</summary>
    public IReadOnlyList<AuditEvent> Read(int limit = 200, string? typeFilter = null)
    {
        if (!File.Exists(_path)) return Array.Empty<AuditEvent>();
        try
        {
            lock (_lock)
            {
                var lines = File.ReadAllLines(_path);
                var events = new List<AuditEvent>(Math.Min(lines.Length, limit));
                for (int i = lines.Length - 1; i >= 0 && events.Count < limit; i--)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var evt = JsonSerializer.Deserialize<AuditEvent>(line);
                        if (evt is null) continue;
                        if (typeFilter is not null && !string.Equals(evt.Type, typeFilter, StringComparison.OrdinalIgnoreCase)) continue;
                        events.Add(evt);
                    }
                    catch { /* skip malformed line */ }
                }
                return events;
            }
        }
        catch
        {
            return Array.Empty<AuditEvent>();
        }
    }

    private void TruncateIfTooLarge()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var info = new FileInfo(_path);
            if (info.Length < 2_000_000) return;  // < 2MB, fine
            var lines = File.ReadAllLines(_path);
            if (lines.Length <= MaxEvents) return;
            var keep = lines.Skip(lines.Length - MaxEvents).ToArray();
            File.WriteAllLines(_path, keep);
        }
        catch { /* best effort */ }
    }
}
