namespace ForexAI.Domain.ValueObjects;

public record MarketSnapshot(
    string Pair,
    string Timeframe,
    decimal CurrentPrice,
    decimal MA20_M15,
    decimal MA50_M15,
    decimal MA20_H1,
    decimal MA50_H1,
    decimal RSI14,
    string RSIDirection,
    string SupportZone,
    string ResistanceZone,
    string Session,
    DateTimeOffset CapturedAt,
    decimal ATR14  = 0m,        // ATR(14) M15 dalam satuan harga; 0 = tidak tersedia (fallback ke 15 pip)
    decimal ADX14  = 0m,        // ADX(14) M15 trend strength 0-100; 0 = tidak tersedia (EA v1.17+)
    string  Regime = "Unknown", // "Trending" | "Ranging" | "Volatile" | "Transitional" | "Unknown"
    decimal MA20_D1 = 0m,       // SMA20 D1 dihitung dari candle cache; 0 = tidak tersedia (D1 candle belum di-push)
    decimal MA50_D1 = 0m        // SMA50 D1 dihitung dari candle cache; 0 = tidak tersedia
);
