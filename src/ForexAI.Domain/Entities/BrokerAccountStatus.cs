namespace ForexAI.Domain.Entities;

public record BrokerAccountStatus(
    string AccountId,
    decimal Equity,
    decimal Balance,
    decimal MarginUsed,
    decimal MarginAvailable,
    int OpenPositionCount
);
