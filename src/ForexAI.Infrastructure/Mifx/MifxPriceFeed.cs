using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Mifx;

/// <summary>
/// Singleton — menyimpan tick terbaru dan posisi open yang dikirim EA MT5.
/// Thread-safe via lock.
/// </summary>
public class MifxPriceFeed : IMarketSpreadGate
{
    private MifxTick? _latest;
    private IReadOnlyList<MifxBrokerPosition> _positions = Array.Empty<MifxBrokerPosition>();

    // Waktu saat backend MENERIMA tick (bukan timestamp dari MT5 server,
    // yang bisa stale/berbeda timezone dari backend).
    private DateTimeOffset _receivedAt = DateTimeOffset.MinValue;
    private readonly object _lock = new();

    // Rolling spread history untuk spike detection — last 60 samples.
    // Pakai Queue (FIFO) supaya cheap append + pop oldest.
    private const int SpreadHistoryMax = 60;
    private readonly Queue<decimal> _spreadHistory = new();

    public MifxTick? Latest
    {
        get { lock (_lock) { return _latest; } }
    }

    /// <summary>
    /// Posisi open EA yang terakhir dilaporkan (EA v1.18+).
    /// List kosong jika belum ada data atau EA versi lama.
    /// </summary>
    public IReadOnlyList<MifxBrokerPosition> Positions
    {
        get { lock (_lock) { return _positions; } }
    }

    /// <summary>
    /// Dianggap "live" jika tick terakhir DITERIMA backend dalam 15 detik terakhir.
    /// Menggunakan waktu backend (bukan waktu MT5 server) agar aman saat MT5
    /// mengirim timestamp lama / timezone berbeda.
    /// </summary>
    public bool IsConnected =>
        Latest != null &&
        (DateTimeOffset.UtcNow - _receivedAt).TotalSeconds < 15;

    /// <summary>
    /// Update tick terbaru. Jika <paramref name="positions"/> bukan null (termasuk list kosong),
    /// daftar posisi juga diperbarui. Null berarti EA versi lama yang tidak mengirim posisi.
    /// </summary>
    public void Update(MifxTick tick, IReadOnlyList<MifxBrokerPosition>? positions = null)
    {
        lock (_lock)
        {
            _latest     = tick;
            _receivedAt = DateTimeOffset.UtcNow;
            if (positions is not null)
                _positions = positions;
            // Track rolling spread for spike detection
            _spreadHistory.Enqueue(tick.Spread);
            while (_spreadHistory.Count > SpreadHistoryMax) _spreadHistory.Dequeue();
        }
    }

    /// <summary>
    /// Rolling average spread (pips) dari last N samples. Return null kalau
    /// data history belum cukup (< 20 samples) — caller decide bagaimana
    /// handle case awal-startup.
    /// </summary>
    public decimal? RollingAvgSpreadPips
    {
        get
        {
            lock (_lock)
            {
                if (_spreadHistory.Count < 20) return null;
                return Math.Round(_spreadHistory.Average(), 2);
            }
        }
    }

    public decimal CurrentSpreadPips => Latest?.Spread ?? 0m;

    /// <summary>
    /// True kalau current spread spike — terlalu lebar dibanding rolling avg.
    /// Threshold: current > 2.5× rolling avg AND current > 1.5 pip absolute
    /// (untuk hindari false positive di rolling avg 0.3 pip jadi 1.0 pip = spike).
    /// </summary>
    public bool IsSpike(out decimal currentSpread, out decimal? rollingAvg)
    {
        currentSpread = CurrentSpreadPips;
        rollingAvg = RollingAvgSpreadPips;
        if (rollingAvg is null) return false;
        return currentSpread > 2.5m * rollingAvg.Value && currentSpread > 1.5m;
    }
}
