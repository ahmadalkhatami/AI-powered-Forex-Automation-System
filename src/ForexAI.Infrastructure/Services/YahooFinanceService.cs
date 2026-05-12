using System.Text.Json;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services;

public class YahooFinanceService : IMarketDataService, ICandleDataService
{
    private readonly HttpClient _http;
    private readonly ILogger<YahooFinanceService> _logger;

    public YahooFinanceService(HttpClient http, ILogger<YahooFinanceService> logger)
    {
        _http = http;
        _logger = logger;
    }

    // "EURUSD" → "EURUSD=X"
    private static string ToYahooSymbol(string pair) =>
        pair.Replace("/", "").ToUpperInvariant() + "=X";

    public async Task<IReadOnlyList<CandleBar>> GetCandlesAsync(string pair, int count = 90)
    {
        var candles = await FetchCandles(ToYahooSymbol(pair));
        return candles.TakeLast(count).ToList().AsReadOnly();
    }

    public async Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        var symbol = ToYahooSymbol(pair);
        var candles = await FetchCandles(symbol);

        if (candles.Count < 20)
            throw new InvalidOperationException($"Not enough Yahoo Finance data for {pair}: got {candles.Count} candles");

        var closes = candles.Select(c => c.Close).ToArray();
        var highs = candles.Select(c => c.High).ToArray();
        var lows = candles.Select(c => c.Low).ToArray();

        var ma20 = Sma(closes, 20);
        var ma50 = closes.Length >= 50 ? Sma(closes, 50) : ma20;
        var rsi = Rsi(closes, 14);
        var rsiDir = closes[^1] > closes[^2] ? "rising" : "falling";

        var lookback = Math.Min(20, candles.Count);
        var support = lows[^lookback..].Min();
        var resistance = highs[^lookback..].Max();

        var last = candles[^1];
        var session = DetectSession(last.Time);

        _logger.LogInformation("Yahoo Finance snapshot {Pair}: price={Price}, RSI={Rsi:F1}, MA20={MA20:F5}",
            pair, last.Close, rsi, ma20);

        return new MarketSnapshot(
            Pair: pair,
            Timeframe: timeframe,
            CurrentPrice: last.Close,
            MA20_M15: ma20,
            MA50_M15: ma50,
            MA20_H1: ma20,
            MA50_H1: ma50,
            RSI14: rsi,
            RSIDirection: rsiDir,
            SupportZone: support.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
            ResistanceZone: resistance.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
            Session: session,
            CapturedAt: DateTimeOffset.FromUnixTimeSeconds(last.Time)
        );
    }

    private async Task<List<CandleBar>> FetchCandles(string symbol)
    {
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?interval=1d&range=1y";
        using var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return ParseCandles(json);
    }

    private static List<CandleBar> ParseCandles(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement
            .GetProperty("chart")
            .GetProperty("result")[0];

        var timestamps = result.GetProperty("timestamp").EnumerateArray()
            .Select(t => t.GetInt64()).ToArray();

        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var opens = quote.GetProperty("open").EnumerateArray().ToArray();
        var highs = quote.GetProperty("high").EnumerateArray().ToArray();
        var lows = quote.GetProperty("low").EnumerateArray().ToArray();
        var closes = quote.GetProperty("close").EnumerateArray().ToArray();

        var candles = new List<CandleBar>(timestamps.Length);
        for (int i = 0; i < timestamps.Length; i++)
        {
            if (closes[i].ValueKind == JsonValueKind.Null) continue;
            if (opens[i].ValueKind == JsonValueKind.Null) continue;
            candles.Add(new CandleBar(
                Time: timestamps[i],
                Open: opens[i].GetDecimal(),
                High: highs[i].GetDecimal(),
                Low: lows[i].GetDecimal(),
                Close: closes[i].GetDecimal()
            ));
        }
        return candles;
    }

    private static decimal Sma(decimal[] data, int period)
        => data[^period..].Average();

    private static decimal Rsi(decimal[] closes, int period)
    {
        if (closes.Length < period + 1) return 50m;

        var changes = new decimal[closes.Length - 1];
        for (int i = 0; i < changes.Length; i++)
            changes[i] = closes[i + 1] - closes[i];

        // Wilder's smoothing: seed with simple average of first `period` values
        var avgGain = changes[..period].Where(c => c > 0).DefaultIfEmpty(0m).Average();
        var avgLoss = changes[..period].Where(c => c < 0).Select(c => -c).DefaultIfEmpty(0m).Average();

        for (int i = period; i < changes.Length; i++)
        {
            avgGain = (avgGain * (period - 1) + Math.Max(changes[i], 0m)) / period;
            avgLoss = (avgLoss * (period - 1) + Math.Abs(Math.Min(changes[i], 0m))) / period;
        }

        if (avgLoss == 0m) return 100m;
        return Math.Round(100m - (100m / (1m + avgGain / avgLoss)), 2);
    }

    private static string DetectSession(long unixSeconds)
    {
        var hour = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime.Hour;
        return hour switch
        {
            >= 0 and < 2 => "Sydney",
            >= 2 and < 8 => "Tokyo",
            >= 8 and < 16 => "London",
            >= 16 and < 20 => "New York",
            _ => "Off-market"
        };
    }
}
