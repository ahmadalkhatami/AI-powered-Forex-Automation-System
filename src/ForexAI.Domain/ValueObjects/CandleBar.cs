namespace ForexAI.Domain.ValueObjects;

public record CandleBar(
    long Time,   // Unix timestamp (seconds)
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close
);
