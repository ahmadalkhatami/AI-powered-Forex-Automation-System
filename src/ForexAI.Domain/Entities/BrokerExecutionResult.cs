namespace ForexAI.Domain.Entities;

public record BrokerExecutionResult(
    bool IsSuccess,
    string? BrokerTradeId,
    string? ErrorMessage,
    decimal ExecutedPrice
);
