using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services.Exness;

public class ExnessLocalMt5MarketDataService : IMarketDataService
{
    private readonly Mt5CommandBus _bus;
    private readonly ILogger<ExnessLocalMt5MarketDataService> _logger;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public ExnessLocalMt5MarketDataService(Mt5CommandBus bus, ILogger<ExnessLocalMt5MarketDataService> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    public async Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        var symbol = pair.Replace("/", "");
        _logger.LogInformation("Requesting FETCH_DATA from MT5 for {Symbol} {Timeframe}", symbol, timeframe);

        var payload = new { Symbol = symbol, Timeframe = "M15", Count = 100 };
        var result = await _bus.SendAsync("FETCH_DATA", payload, Timeout);

        if (!result.Success)
            throw new Exception($"MT5 returned error: {result.Error}");

        if (result.Candles == null || result.Candles.Count < 20)
            throw new Exception("MT5 returned insufficient candle data.");

        // Also request H1 candles
        var payloadH1 = new { Symbol = symbol, Timeframe = "H1", Count = 100 };
        var resultH1 = await _bus.SendAsync("FETCH_DATA", payloadH1, Timeout);

        var m15Close = result.Candles.Select(c => (decimal)c.Close).ToArray();
        var h1Close = resultH1.Success && resultH1.Candles?.Count > 20
            ? resultH1.Candles.Select(c => (decimal)c.Close).ToArray()
            : m15Close;

        var m15Lows = result.Candles.Select(c => (decimal)c.Low).ToArray();
        var m15Highs = result.Candles.Select(c => (decimal)c.High).ToArray();

        decimal currentPrice = m15Close.Last();
        decimal ma20_m15 = TechnicalIndicators.CalculateSMA(m15Close, 20);
        decimal ma50_m15 = TechnicalIndicators.CalculateSMA(m15Close, 50);
        decimal ma20_h1 = TechnicalIndicators.CalculateSMA(h1Close, 20);
        decimal ma50_h1 = TechnicalIndicators.CalculateSMA(h1Close, 50);

        var (rsi, rsiDir) = TechnicalIndicators.CalculateRSI(m15Close, 14);
        var (support, resistance) = TechnicalIndicators.FindZones(
            m15Lows.TakeLast(50).ToArray(),
            m15Highs.TakeLast(50).ToArray());

        return new MarketSnapshot(
            Pair: pair,
            Timeframe: timeframe,
            CurrentPrice: currentPrice,
            MA20_M15: Math.Round(ma20_m15, 5),
            MA50_M15: Math.Round(ma50_m15, 5),
            MA20_H1: Math.Round(ma20_h1, 5),
            MA50_H1: Math.Round(ma50_h1, 5),
            RSI14: Math.Round(rsi, 2),
            RSIDirection: rsiDir,
            SupportZone: support,
            ResistanceZone: resistance,
            Session: GetCurrentSession(),
            CapturedAt: DateTimeOffset.UtcNow
        );
    }

    private static string GetCurrentSession()
    {
        var h = DateTime.UtcNow.Hour;
        if (h >= 8 && h < 16) return "London";
        if (h >= 13 && h < 21) return "New York";
        if (h >= 0 && h < 8) return "Tokyo/Sydney";
        return "Asian/Pacific";
    }
}
