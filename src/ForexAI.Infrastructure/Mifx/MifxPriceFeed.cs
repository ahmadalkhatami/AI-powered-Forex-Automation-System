using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Mifx;

/// <summary>
/// Singleton — menyimpan tick terbaru dan posisi open yang dikirim EA MT5.
/// Thread-safe via lock.
/// </summary>
public class MifxPriceFeed
{
    private MifxTick? _latest;
    private IReadOnlyList<MifxBrokerPosition> _positions = Array.Empty<MifxBrokerPosition>();

    // Waktu saat backend MENERIMA tick (bukan timestamp dari MT5 server,
    // yang bisa stale/berbeda timezone dari backend).
    private DateTimeOffset _receivedAt = DateTimeOffset.MinValue;
    private readonly object _lock = new();

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
        }
    }
}
