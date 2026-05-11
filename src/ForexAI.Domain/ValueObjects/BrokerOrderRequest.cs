namespace ForexAI.Domain.ValueObjects;

public record BrokerOrderRequest(
    string Instrument,
    bool IsBuy,
    decimal LotSize,
    decimal StopLoss,
    decimal TakeProfit
);
