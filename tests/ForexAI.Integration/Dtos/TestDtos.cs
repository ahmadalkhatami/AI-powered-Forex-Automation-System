namespace ForexAI.Integration.Dtos;

record TradeSignalDto(Guid Id, string Signal, decimal ConfidenceScore);

// Matches full serialized shape of RiskValidation domain type for round-trip to /api/trade/execute
record RiskValidationDto(
    string Decision,
    string PositionDecision,
    bool IsGo,
    TradeParametersDto? ValidatedParameters,
    string[] CautionNotes,
    string[] NoGoReasons);

// Matches TradeParameters value object (all fields required for deserialization on execute endpoint)
record TradeParametersDto(
    decimal Entry,
    decimal StopLoss,
    int StopLossPips,
    decimal TakeProfit,
    int TakeProfitPips,
    decimal LotSize,
    decimal RiskAmount,
    decimal PotentialProfit,
    decimal RiskRewardRatio);

record TradePositionDto(string TradeId, string Status, string Pair);
