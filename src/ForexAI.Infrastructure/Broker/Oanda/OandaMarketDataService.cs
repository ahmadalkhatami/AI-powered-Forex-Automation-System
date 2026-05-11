using System.Text.Json;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure.Broker.Oanda.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForexAI.Infrastructure.Broker.Oanda;

public class OandaMarketDataService : IMarketDataService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly OandaSettings _settings;
    private readonly ILogger<OandaMarketDataService> _logger;

    public OandaMarketDataService(
        HttpClient http,
        IOptions<OandaSettings> settings,
        ILogger<OandaMarketDataService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        var instrument = ToOandaInstrument(pair);

        var (m15Candles, h1Candles) = await FetchBothGranularitiesAsync(instrument);

        var m15Closes = m15Candles.Select(c => decimal.Parse(c.Mid.Close)).ToArray();
        var h1Closes = h1Candles.Select(c => decimal.Parse(c.Mid.Close)).ToArray();
        var m15Highs = m15Candles.Select(c => decimal.Parse(c.Mid.High)).ToArray();
        var m15Lows = m15Candles.Select(c => decimal.Parse(c.Mid.Low)).ToArray();

        var latest = m15Candles.Last();
        var currentPrice = decimal.Parse(latest.Mid.Close);
        var capturedAt = DateTimeOffset.Parse(latest.Time);

        var rsi = ForexIndicators.Rsi(m15Closes, 14);
        var rsiDirection = m15Closes.Length >= 2 && m15Closes[^1] > m15Closes[^2]
            ? "rising" : "falling";

        var (supportZone, resistanceZone) = ForexIndicators.DetectZones(m15Highs, m15Lows);
        var session = ForexIndicators.DetectSession(capturedAt);

        _logger.LogInformation(
            "OANDA snapshot {Pair} @ {Price} | RSI={Rsi} ({Dir}) | Session={Session}",
            pair, currentPrice, rsi, rsiDirection, session);

        return new MarketSnapshot(
            Pair: pair,
            Timeframe: timeframe,
            CurrentPrice: currentPrice,
            MA20_M15: ForexIndicators.Sma(m15Closes, 20),
            MA50_M15: ForexIndicators.Sma(m15Closes, 50),
            MA20_H1: ForexIndicators.Sma(h1Closes, 20),
            MA50_H1: ForexIndicators.Sma(h1Closes, 50),
            RSI14: rsi,
            RSIDirection: rsiDirection,
            SupportZone: supportZone,
            ResistanceZone: resistanceZone,
            Session: session,
            CapturedAt: capturedAt);
    }

    private async Task<(List<OandaCandle> M15, List<OandaCandle> H1)> FetchBothGranularitiesAsync(
        string instrument)
    {
        var m15Task = FetchCandlesAsync(instrument, "M15", 60);
        var h1Task = FetchCandlesAsync(instrument, "H1", 60);
        await Task.WhenAll(m15Task, h1Task);
        return (await m15Task, await h1Task);
    }

    private async Task<List<OandaCandle>> FetchCandlesAsync(
        string instrument, string granularity, int count)
    {
        var url = $"/v3/instruments/{instrument}/candles?count={count}&granularity={granularity}&price=M";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OandaCandleResponse>(body, JsonOpts)
            ?? throw new InvalidOperationException($"Empty candle response for {instrument}/{granularity}");

        return result.Candles.Where(c => c.Complete).ToList();
    }

    private static string ToOandaInstrument(string pair)
    {
        pair = pair.Replace("/", "").ToUpperInvariant();
        return pair.Length == 6 ? pair[..3] + "_" + pair[3..] : pair;
    }
}
