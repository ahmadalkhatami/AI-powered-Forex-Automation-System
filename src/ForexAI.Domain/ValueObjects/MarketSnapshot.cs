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
    DateTimeOffset CapturedAt
);
