using ForexAI.Domain.Entities;

namespace ForexAI.Domain.Interfaces;

public interface ISignalRepository
{
    Task<TradeSignal?> GetLatestAsync(string pair);
    Task<TradeSignal?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<TradeSignal>> GetAllAsync();
    Task SaveAsync(TradeSignal signal);
}
