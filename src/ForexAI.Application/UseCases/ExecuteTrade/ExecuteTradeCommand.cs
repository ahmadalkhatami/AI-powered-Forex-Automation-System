using ForexAI.Domain.Entities;
using ForexAI.Domain.ValueObjects;
using MediatR;

namespace ForexAI.Application.UseCases.ExecuteTrade;

public record ExecuteTradeCommand(
    Guid SignalId,
    RiskValidation RiskValidation,
    decimal PeakEquity,
    decimal CurrentEquity,
    string Mode = "SIMULATION"
) : IRequest<TradePosition>;
