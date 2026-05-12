using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Integration;

internal class NullCandleDataService : ICandleDataService
{
    public Task<IReadOnlyList<CandleBar>> GetCandlesAsync(string pair, int count = 90) =>
        Task.FromResult<IReadOnlyList<CandleBar>>(Array.Empty<CandleBar>());
}
