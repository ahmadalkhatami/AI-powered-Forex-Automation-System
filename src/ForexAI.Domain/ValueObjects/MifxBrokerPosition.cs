namespace ForexAI.Domain.ValueObjects;

/// <summary>
/// Snapshot satu posisi open yang dilaporkan EA MT5 per tick.
/// Digunakan untuk real-time PnL sync dan auto-close detection.
/// </summary>
public record MifxBrokerPosition(
    long    Ticket,       // Nomor tiket MT5
    string  Type,         // "BUY" | "SELL"
    string  Symbol,       // e.g. "EURUSD.m"
    decimal Lots,         // Volume lot
    decimal OpenPrice,    // Harga saat order dibuka
    decimal Profit,       // Floating PnL dalam USD (dari MIFX account currency)
    int     Pips          // Pip delta dari harga buka
);
