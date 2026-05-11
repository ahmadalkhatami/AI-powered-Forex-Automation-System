using System.Text.Json;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure.Broker.Oanda;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForexAI.Infrastructure.Broker.AlphaVantage;

public class AlphaVantageMarketDataService : IMarketDataService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly AlphaVantageSettings _settings;
    private readonly ILogger<AlphaVantageMarketDataService> _logger;

    public AlphaVantageMarketDataService(
        HttpClient http,
        IOptions<AlphaVantageSettings> settings,
        ILogger<AlphaVantageMarketDataService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        var (fromSymbol, toSymbol) = ParsePair(pair);

        var m15Task = FetchCandlesAsync(fromSymbol, toSymbol, "15min");
        var h1Task = FetchCandlesAsync(fromSymbol, toSymbol, "60min");
        await Task.WhenAll(m15Task, h1Task);

        var m15Candles = await m15Task;
        var h1Candles = await h1Task;

        var m15Closes = m15Candles.Select(c => c.Close).ToArray();
        var h1Closes = h1Candles.Select(c => c.Close).ToArray();
        var m15Highs = m15Candles.Select(c => c.High).ToArray();
        var m15Lows = m15Candles.Select(c => c.Low).ToArray();

        var latest = m15Candles.Last();
        var rsi = ForexIndicators.Rsi(m15Closes, 14);
        var rsiDirection = m15Closes.Length >= 2 && m15Closes[^1] > m15Closes[^2] ? "rising" : "falling";
        var (support, resistance) = ForexIndicators.DetectZones(m15Highs, m15Lows);
        var session = ForexIndicators.DetectSession(DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "AlphaVantage snapshot {Pair} @ {Price} | RSI={Rsi} ({Dir}) | Session={Session}",
            pair, latest.Close, rsi, rsiDirection, session);

        return new MarketSnapshot(
            Pair: pair,
            Timeframe: timeframe,
            CurrentPrice: latest.Close,
            MA20_M15: ForexIndicators.Sma(m15Closes, 20),
            MA50_M15: ForexIndicators.Sma(m15Closes, 50),
            MA20_H1: ForexIndicators.Sma(h1Closes, 20),
            MA50_H1: ForexIndicators.Sma(h1Closes, 50),
            RSI14: rsi,
            RSIDirection: rsiDirection,
            SupportZone: support,
            ResistanceZone: resistance,
            Session: session,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    private async Task<List<(decimal Open, decimal High, decimal Low, decimal Close)>> FetchCandlesAsync(
        string fromSymbol, string toSymbol, string interval)
    {
        var url = $"/query?function=FX_INTRADAY&from_symbol={fromSymbol}&to_symbol={toSymbol}" +
                  $"&interval={interval}&outputsize=compact&apikey={_settings.ApiKey}";

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var timeSeriesKey = $"Time Series FX ({interval})";
        if (!doc.RootElement.TryGetProperty(timeSeriesKey, out var timeSeries))
            throw new InvalidOperationException(
                $"AlphaVantage response missing '{timeSeriesKey}'. " +
                $"Possible rate limit hit. Response: {body[..Math.Min(200, body.Length)]}");

        var candles = new List<(decimal, decimal, decimal, decimal)>();
        foreach (var entry in timeSeries.EnumerateObject())
        {
            var o = decimal.Parse(entry.Value.GetProperty("1. open").GetString()!);
            var h = decimal.Parse(entry.Value.GetProperty("2. high").GetString()!);
            var l = decimal.Parse(entry.Value.GetProperty("3. low").GetString()!);
            var c = decimal.Parse(entry.Value.GetProperty("4. close").GetString()!);
            candles.Add((o, h, l, c));
        }

        // Response is newest-first; reverse to chronological order then take last 60
        candles.Reverse();
        return candles.TakeLast(60).ToList();
    }

    private static (string From, string To) ParsePair(string pair)
    {
        pair = pair.Replace("/", "").ToUpperInvariant();
        return pair.Length == 6 ? (pair[..3], pair[3..]) : ("EUR", "USD");
    }
}
