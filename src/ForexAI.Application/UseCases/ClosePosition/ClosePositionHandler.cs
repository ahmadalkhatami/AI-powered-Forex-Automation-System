using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ForexAI.Application.UseCases.ClosePosition;

public class ClosePositionHandler : IRequestHandler<ClosePositionCommand, TradePosition>
{
    private readonly ITradePositionRepository _positions;
    private readonly IBrokerService _broker;
    private readonly ILogger<ClosePositionHandler> _logger;

    public ClosePositionHandler(
        ITradePositionRepository positions,
        IBrokerService broker,
        ILogger<ClosePositionHandler> logger)
    {
        _positions = positions;
        _broker = broker;
        _logger = logger;
    }

    public async Task<TradePosition> Handle(ClosePositionCommand request, CancellationToken cancellationToken)
    {
        var all = await _positions.GetAllAsync();
        var position = all.FirstOrDefault(p =>
            string.Equals(p.TradeId, request.TradeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Position {request.TradeId} not found");

        var exitPrice = request.ExitPrice;
        if (_broker.IsLive && IsMifxPosition(position))
        {
            var closeResult = await _broker.ClosePositionAsync(position, cancellationToken);
            if (!closeResult.IsSuccess)
                throw new InvalidOperationException($"MIFX close failed: {closeResult.ErrorMessage ?? "unknown error"}");

            if (closeResult.ExecutedPrice > 0m)
                exitPrice = closeResult.ExecutedPrice;
        }

        position.CloseManually(request.Outcome, exitPrice);
        await _positions.SaveAsync(position);

        _logger.LogInformation("Position closed manually: {TradeId} → {Outcome}, exitPrice={ExitPrice}, pnl={Pnl}",
            position.TradeId, position.Status, exitPrice, position.FloatingPnl);

        return position;
    }

    private static bool IsMifxPosition(TradePosition position) =>
        string.Equals(position.Mode, "MIFX_DEMO", StringComparison.OrdinalIgnoreCase) &&
        position.ExternalTradeId?.StartsWith("MIFX-", StringComparison.OrdinalIgnoreCase) == true;
}
