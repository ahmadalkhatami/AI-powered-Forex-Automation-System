using ForexAI.Domain.ValueObjects;

namespace ForexAI.Domain.Interfaces;

public interface IMarketDataService
{
    Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe);
}
