using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Mifx;

/// <summary>
/// ICandleDataService backed by MifxCandleFeed (EA push).
/// Default timeframe = "M15". Untuk timeframe spesifik, gunakan GetCandlesAsync(pair, timeframe, count).
/// Return list kosong jika EA belum kirim candle untuk pair/timeframe tersebut.
/// </summary>
public class MifxCandleDataService : ICandleDataService
{
    private readonly MifxCandleFeed _feed;

    public MifxCandleDataService(MifxCandleFeed feed) => _feed = feed;

    public Task<IReadOnlyList<CandleBar>> GetCandlesAsync(string pair, int count = 90)
        => GetCandlesAsync(pair, "M15", count);

    public Task<IReadOnlyList<CandleBar>> GetCandlesAsync(string pair, string timeframe, int count = 90)
        => Task.FromResult(_feed.Get(pair, timeframe, count));
}
