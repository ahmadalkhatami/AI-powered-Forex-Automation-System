using ForexAI.Domain.ValueObjects;

namespace ForexAI.API.Models;

public record ExecuteTradeRequest(
    Guid SignalId,
    RiskValidation RiskValidation,
    decimal PeakEquity,
    decimal CurrentEquity,
    string Mode = "SIMULATION",
    /// <summary>
    /// Override risk per trade (mis. 0.05 = 5%) untuk Nano mode — user adjust via slider
    /// di dashboard sebelum approve. Kalau null/0, pakai tier default.
    /// </summary>
    decimal? RiskPctOverride = null);
