using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using MediatR;

namespace ForexAI.Application.UseCases.GetPositionStatus;

public class GetPositionStatusHandler : IRequestHandler<GetPositionStatusQuery, TradePosition?>
{
    private readonly ITradePositionRepository _positions;

    public GetPositionStatusHandler(ITradePositionRepository positions)
    {
        _positions = positions;
    }

    public Task<TradePosition?> Handle(GetPositionStatusQuery request, CancellationToken cancellationToken)
        => _positions.GetActiveByPairAsync(request.Pair);
}
