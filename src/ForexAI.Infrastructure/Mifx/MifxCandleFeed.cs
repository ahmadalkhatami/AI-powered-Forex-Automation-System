using System.Text.Json;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Mifx;

/// <summary>
/// Singleton — menyimpan candle history per pair/timeframe yang dikirim EA MT5.
/// Thread-safe via lock. Replace seluruh list saat EA push update terbaru.
/// Persist ke disk supaya chart tetap punya data setelah backend restart
/// (EA hanya push candle saat new bar / EA startup, jadi cache in-memory saja
///  akan kosong sampai bar berikutnya kalau backend restart di tengah).
/// </summary>
public class MifxCandleFeed
{
    private readonly Dictionary<string, IReadOnlyList<CandleBar>> _cache = new();
    private readonly Dictionary<string, DateTimeOffset> _receivedAt = new();
    private readonly object _lock = new();
    private readonly IModeService _mode;

    private string PersistPath
    {
        get
        {
            var dir = ProjectPaths.GetImplementationArtifactsDir(_mode.CurrentMode);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "mifx-candle-cache.json");
        }
    }

    public MifxCandleFeed(IModeService mode)
    {
        _mode = mode;
        _mode.ModeChanged += (_, _) =>
        {
            // Cache di-clear saat mode change — load ulang dari path mode baru.
            lock (_lock) { _cache.Clear(); _receivedAt.Clear(); }
            Load();
        };
        Load();
    }

    public void Upsert(string pair, string timeframe, IReadOnlyList<CandleBar> bars)
    {
        var key = MakeKey(pair, timeframe);
        lock (_lock)
        {
            _cache[key]      = bars;
            _receivedAt[key] = DateTimeOffset.UtcNow;
            Save_NoLock();
        }
    }

    /// <summary>
    /// Ambil sampai <paramref name="count"/> bar terbaru untuk pair/timeframe.
    /// Return list kosong jika belum ada data.
    /// </summary>
    public IReadOnlyList<CandleBar> Get(string pair, string timeframe, int count)
    {
        var key = MakeKey(pair, timeframe);
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out var bars)) return Array.Empty<CandleBar>();
            if (bars.Count <= count) return bars;
            return bars.Skip(bars.Count - count).ToList();
        }
    }

    public bool HasData(string pair, string timeframe)
    {
        var key = MakeKey(pair, timeframe);
        lock (_lock)
        {
            return _cache.ContainsKey(key) && _cache[key].Count > 0;
        }
    }

    public DateTimeOffset? LastReceivedAt(string pair, string timeframe)
    {
        var key = MakeKey(pair, timeframe);
        lock (_lock)
        {
            return _receivedAt.TryGetValue(key, out var t) ? t : null;
        }
    }

    private static string MakeKey(string pair, string timeframe) =>
        $"{Normalize(pair)}_{timeframe.ToUpperInvariant()}";

    private static string Normalize(string pair) =>
        pair.Replace("/", "").Replace(".", "").ToUpperInvariant()[..Math.Min(6, pair.Length)];

    private record PersistedCache(
        Dictionary<string, List<CandleBar>> Cache,
        Dictionary<string, DateTimeOffset>  ReceivedAt);

    private void Save_NoLock()
    {
        try
        {
            var snapshot = new PersistedCache(
                _cache.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
                new Dictionary<string, DateTimeOffset>(_receivedAt));
            var json = JsonSerializer.Serialize(snapshot);
            File.WriteAllText(PersistPath, json);
        }
        catch
        {
            // Silently swallow — persistence is best-effort
        }
    }

    private void Load()
    {
        if (!File.Exists(PersistPath)) return;
        try
        {
            var json = File.ReadAllText(PersistPath);
            var snapshot = JsonSerializer.Deserialize<PersistedCache>(json);
            if (snapshot is null) return;
            lock (_lock)
            {
                foreach (var kv in snapshot.Cache)
                    _cache[kv.Key] = kv.Value;
                foreach (var kv in snapshot.ReceivedAt)
                    _receivedAt[kv.Key] = kv.Value;
            }
        }
        catch
        {
            // Corrupt cache file — skip, EA push akan re-populate
        }
    }
}
