namespace ForexAI.Domain.ValueObjects;

/// <summary>
/// Hasil eksekusi order ke broker. Memungkinkan handler membedakan
/// EA tidak konek vs broker tolak order (SL terlalu dekat, dll) vs timeout.
/// </summary>
public record BrokerOrderResult(
    bool    Success,
    string? ExternalId,        // "MIFX-{ticket}" kalau success
    string  StatusReason,      // "FILLED" | "FAILED" | "TIMEOUT" | "DISCONNECTED"
    int     BrokerRetcode = 0, // MT5 retcode (10016 = invalid stops, dll)
    string? ErrorMessage = null
)
{
    public static BrokerOrderResult Disconnected() =>
        new(false, null, "DISCONNECTED", 0, "MIFX EA tidak terkoneksi");

    public static BrokerOrderResult Filled(string externalId) =>
        new(true, externalId, "FILLED");

    public static BrokerOrderResult Failed(int retcode, string? message = null) =>
        new(false, null, "FAILED", retcode, message ?? DescribeRetcode(retcode));

    public static BrokerOrderResult TimedOut() =>
        new(false, null, "TIMEOUT", -1, "EA tidak merespons dalam 30 detik");

    /// <summary>Decode MT5 retcode ke pesan yang dimengerti user.</summary>
    private static string DescribeRetcode(int retcode) => retcode switch
    {
        10004 => "Requote — harga berubah",
        10006 => "Order ditolak oleh broker",
        10013 => "Volume invalid (lot size salah)",
        10014 => "Volume di luar batas yang diperbolehkan",
        10015 => "Harga invalid",
        10016 => "Stop level terlalu dekat — SL/TP harus lebih jauh dari harga",
        10017 => "Trading dimatikan untuk simbol ini",
        10018 => "Market closed",
        10019 => "Saldo tidak cukup",
        10021 => "Tidak ada quote saat ini",
        10027 => "Autotrading dinonaktifkan di MT5",
        10030 => "Filling mode tidak didukung broker",
        _     => $"Broker error (retcode {retcode})"
    };
}
