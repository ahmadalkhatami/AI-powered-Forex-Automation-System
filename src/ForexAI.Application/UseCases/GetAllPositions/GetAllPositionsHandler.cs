using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using MediatR;

namespace ForexAI.Application.UseCases.GetAllPositions;

public class GetAllPositionsHandler : IRequestHandler<GetAllPositionsQuery, IReadOnlyList<TradePosition>>
{
    private readonly ITradePositionRepository _positions;

    public GetAllPositionsHandler(ITradePositionRepository positions)
    {
        _positions = positions;
    }

    public Task<IReadOnlyList<TradePosition>> Handle(
        GetAllPositionsQuery request,
        CancellationToken cancellationToken)
        => _positions.GetAllAsync();
}
