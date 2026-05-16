using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Integration;

internal class FakeMarketDataService : IMarketDataService
{
    public Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        // Bullish setup: price > MA20 > MA50 di kedua TF, RSI rising di mid-range
        var snap = new MarketSnapshot(
            Pair: pair,
            Timeframe: timeframe,
            CurrentPrice: 1.0850m,
            MA20_M15: 1.0840m,
            MA50_M15: 1.0820m,
            MA20_H1:  1.0830m,
            MA50_H1:  1.0800m,
            RSI14:    58m,
            RSIDirection: "rising",
            SupportZone:    "1.0830",
            ResistanceZone: "1.0890",
            Session: "LONDON",
            CapturedAt: DateTimeOffset.UtcNow,
            ATR14:  0.0008m,
            ADX14:  28m,
            Regime: "Trending",
            MA20_D1: 1.0810m,
            MA50_D1: 1.0780m);

        return Task.FromResult(snap);
    }
}
