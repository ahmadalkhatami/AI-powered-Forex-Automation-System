namespace ForexAI.Domain.ValueObjects;

public record TradeParameters(
    decimal Entry,
    decimal StopLoss,
    int StopLossPips,
    decimal TakeProfit,
    int TakeProfitPips,
    decimal LotSize,
    decimal RiskAmount,
    decimal PotentialProfit,
    decimal RiskRewardRatio
);
