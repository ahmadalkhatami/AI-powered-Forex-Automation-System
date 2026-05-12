using ForexAI.Domain.ValueObjects;

namespace ForexAI.Domain.Interfaces;

public interface ICandleDataService
{
    Task<IReadOnlyList<CandleBar>> GetCandlesAsync(string pair, int count = 90);
}
