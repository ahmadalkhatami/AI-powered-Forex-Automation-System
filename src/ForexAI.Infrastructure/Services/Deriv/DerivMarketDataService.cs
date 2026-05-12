using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services.Deriv;

public class DerivMarketDataService : IMarketDataService
{
    private readonly DerivWebSocketClient _client;
    private readonly ILogger<DerivMarketDataService> _logger;

    public DerivMarketDataService(DerivWebSocketClient client, ILogger<DerivMarketDataService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        var symbol = ToDerivSymbol(pair);
        var granularity = ToGranularity(timeframe);
        var granularityH1 = 3600;

        _logger.LogInformation("[Deriv] Fetching {Symbol} {Timeframe} candles...", symbol, timeframe);

        // Fetch M15 candles
        var m15Response = await _client.SendAsync(new
        {
            ticks_history = symbol,
            adjust_dst_for_closed_markets = 1,
            count = 100,
            end = "latest",
            granularity = granularity,
            start = 1,
            style = "candles"
        });

        // Fetch H1 candles
        var h1Response = await _client.SendAsync(new
        {
            ticks_history = symbol,
            adjust_dst_for_closed_markets = 1,
            count = 100,
            end = "latest",
            granularity = granularityH1,
            start = 1,
            style = "candles"
        });

        if (m15Response.TryGetProperty("error", out var err))
            throw new Exception($"Deriv data error: {err.GetProperty("message").GetString()}");

        var m15Candles = ParseCandles(m15Response);
        var h1Candles = ParseCandles(h1Response);

        if (m15Candles.Count < 20)
            throw new Exception($"Insufficient M15 candles from Deriv: {m15Candles.Count}");

        var m15Close = m15Candles.Select(c => c.Close).ToArray();
        var h1Close  = h1Candles.Count >= 20 ? h1Candles.Select(c => c.Close).ToArray() : m15Close;
        var m15Lows  = m15Candles.Select(c => c.Low).ToArray();
        var m15Highs = m15Candles.Select(c => c.High).ToArray();

        decimal currentPrice = m15Close.Last();
        decimal ma20M15 = TechnicalIndicators.CalculateSMA(m15Close, 20);
        decimal ma50M15 = TechnicalIndicators.CalculateSMA(m15Close, 50);
        decimal ma20H1  = TechnicalIndicators.CalculateSMA(h1Close, 20);
        decimal ma50H1  = TechnicalIndicators.CalculateSMA(h1Close, 50);

        var (rsi, rsiDir) = TechnicalIndicators.CalculateRSI(m15Close, 14);
        var (support, resistance) = TechnicalIndicators.FindZones(
            m15Lows.TakeLast(50).ToArray(),
            m15Highs.TakeLast(50).ToArray());

        _logger.LogInformation("[Deriv] Snapshot ready: {Symbol} @ {Price}", symbol, currentPrice);

        return new MarketSnapshot(
            Pair:           pair,
            Timeframe:      timeframe,
            CurrentPrice:   currentPrice,
            MA20_M15:       Math.Round(ma20M15, 5),
            MA50_M15:       Math.Round(ma50M15, 5),
            MA20_H1:        Math.Round(ma20H1, 5),
            MA50_H1:        Math.Round(ma50H1, 5),
            RSI14:          Math.Round(rsi, 2),
            RSIDirection:   rsiDir,
            SupportZone:    support,
            ResistanceZone: resistance,
            Session:        GetCurrentSession(),
            CapturedAt:     DateTimeOffset.UtcNow
        );
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<(decimal Open, decimal High, decimal Low, decimal Close)> ParseCandles(
        System.Text.Json.JsonElement response)
    {
        var result = new List<(decimal, decimal, decimal, decimal)>();
        if (!response.TryGetProperty("candles", out var candles)) return result;

        foreach (var c in candles.EnumerateArray())
        {
            result.Add((
                (decimal)c.GetProperty("open").GetDouble(),
                (decimal)c.GetProperty("high").GetDouble(),
                (decimal)c.GetProperty("low").GetDouble(),
                (decimal)c.GetProperty("close").GetDouble()
            ));
        }
        return result;
    }

    private static string ToDerivSymbol(string pair)
    {
        var clean = pair.Replace("/", "").Replace("-", "").ToUpper();
        return $"frx{clean}";
    }

    private static int ToGranularity(string tf) => tf.ToUpper() switch
    {
        "M1"  => 60,
        "M5"  => 300,
        "M15" => 900,
        "M30" => 1800,
        "H1"  => 3600,
        "H4"  => 14400,
        "D1"  => 86400,
        _     => 900
    };

    private static string GetCurrentSession()
    {
        var h = DateTime.UtcNow.Hour;
        if (h >= 8 && h < 16)  return "London";
        if (h >= 13 && h < 21) return "New York";
        if (h >= 0 && h < 8)   return "Tokyo/Sydney";
        return "Asian/Pacific";
    }
}
