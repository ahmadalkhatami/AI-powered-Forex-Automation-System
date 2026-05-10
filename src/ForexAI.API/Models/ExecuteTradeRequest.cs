using ForexAI.Domain.ValueObjects;

namespace ForexAI.API.Models;

public record ExecuteTradeRequest(
    Guid SignalId,
    RiskValidation RiskValidation,
    decimal PeakEquity,
    decimal CurrentEquity,
    string Mode = "SIMULATION");
