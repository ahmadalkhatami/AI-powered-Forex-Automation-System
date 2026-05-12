using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using MediatR;

namespace ForexAI.Application.UseCases.ClosePosition;

public record ClosePositionCommand(
    string TradeId,
    TradeStatus Outcome,   // CLOSED_WIN or CLOSED_LOSS
    decimal ExitPrice
) : IRequest<TradePosition>;
