using ForexAI.Domain.Entities;
using MediatR;

namespace ForexAI.Application.UseCases.GetAllPositions;

public record GetAllPositionsQuery : IRequest<IReadOnlyList<TradePosition>>;
