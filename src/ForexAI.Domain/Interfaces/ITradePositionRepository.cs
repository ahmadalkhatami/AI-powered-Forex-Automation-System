using ForexAI.Domain.Entities;

namespace ForexAI.Domain.Interfaces;

public interface ITradePositionRepository
{
    Task<TradePosition?> GetActiveByPairAsync(string pair);
    Task<IReadOnlyList<TradePosition>> GetOpenPositionsAsync();
    Task SaveAsync(TradePosition position);
    Task<int> CountOpenPositionsAsync();
}
