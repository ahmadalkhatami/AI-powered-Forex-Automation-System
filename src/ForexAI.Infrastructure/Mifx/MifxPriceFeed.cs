using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Mifx;

/// <summary>
/// Singleton — menyimpan tick terbaru yang dikirim EA MT5.
/// Thread-safe via volatile write.
/// </summary>
public class MifxPriceFeed
{
    private volatile MifxTick? _latest;
    // Waktu saat backend MENERIMA tick (bukan timestamp dari MT5 server,
    // yang bisa stale/berbeda timezone dari backend).
    private DateTimeOffset _receivedAt = DateTimeOffset.MinValue;
    private readonly object _lock = new();

    public MifxTick? Latest => _latest;

    /// <summary>
    /// Dianggap "live" jika tick terakhir DITERIMA backend dalam 15 detik terakhir.
    /// Menggunakan waktu backend (bukan waktu MT5 server) agar aman saat MT5
    /// mengirim timestamp lama / timezone berbeda.
    /// </summary>
    public bool IsConnected =>
        _latest != null &&
        (DateTimeOffset.UtcNow - _receivedAt).TotalSeconds < 15;

    public void Update(MifxTick tick)
    {
        lock (_lock)
        {
            _latest     = tick;
            _receivedAt = DateTimeOffset.UtcNow;
        }
    }
}
