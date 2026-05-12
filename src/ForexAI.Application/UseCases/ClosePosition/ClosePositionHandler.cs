using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ForexAI.Application.UseCases.ClosePosition;

public class ClosePositionHandler : IRequestHandler<ClosePositionCommand, TradePosition>
{
    private readonly ITradePositionRepository _positions;
    private readonly ILogger<ClosePositionHandler> _logger;

    public ClosePositionHandler(ITradePositionRepository positions, ILogger<ClosePositionHandler> logger)
    {
        _positions = positions;
        _logger = logger;
    }

    public async Task<TradePosition> Handle(ClosePositionCommand request, CancellationToken cancellationToken)
    {
        var all = await _positions.GetAllAsync();
        var position = all.FirstOrDefault(p =>
            string.Equals(p.TradeId, request.TradeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Position {request.TradeId} not found");

        position.CloseManually(request.Outcome, request.ExitPrice);
        await _positions.SaveAsync(position);

        _logger.LogInformation("Position closed manually: {TradeId} → {Outcome}, exitPrice={ExitPrice}, pnl={Pnl}",
            position.TradeId, position.Status, request.ExitPrice, position.FloatingPnl);

        return position;
    }
}
