using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;

namespace ForexAI.Infrastructure.Services.Oanda;

public class OandaMarketDataService : IMarketDataService
{
    private readonly HttpClient _httpClient;

    public OandaMarketDataService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        var baseUrl = configuration["BrokerIntegration:Oanda:BaseUrl"] ?? "https://api-fxpractice.oanda.com";
        var token = configuration["BrokerIntegration:Oanda:BearerToken"];
        
        _httpClient.BaseAddress = new Uri(baseUrl);
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        string oandaPair = pair.Replace("/", "_").ToUpper();
        if (!oandaPair.Contains('_'))
        {
            oandaPair = oandaPair.Insert(3, "_"); // EURUSD -> EUR_USD
        }

        var m15Candles = await FetchCandlesAsync(oandaPair, "M15", 100);
        var h1Candles = await FetchCandlesAsync(oandaPair, "H1", 100);

        if (m15Candles.Count == 0 || h1Candles.Count == 0)
        {
            throw new Exception("Failed to fetch market data from OANDA.");
        }

        var m15Close = m15Candles.Select(c => c.Close).ToArray();
        var h1Close = h1Candles.Select(c => c.Close).ToArray();
        var m15Lows = m15Candles.Select(c => c.Low).ToArray();
        var m15Highs = m15Candles.Select(c => c.High).ToArray();

        decimal currentPrice = m15Close.Last();
        decimal ma20_m15 = TechnicalIndicators.CalculateSMA(m15Close, 20);
        decimal ma50_m15 = TechnicalIndicators.CalculateSMA(m15Close, 50);
        decimal ma20_h1 = TechnicalIndicators.CalculateSMA(h1Close, 20);
        decimal ma50_h1 = TechnicalIndicators.CalculateSMA(h1Close, 50);
        
        var (rsi, rsiDir) = TechnicalIndicators.CalculateRSI(m15Close, 14);
        var (support, resistance) = TechnicalIndicators.FindZones(m15Lows.TakeLast(50).ToArray(), m15Highs.TakeLast(50).ToArray());

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

    private string GetCurrentSession()
    {
        var hour = DateTime.UtcNow.Hour;
        if (hour >= 8 && hour < 16) return "London";
        if (hour >= 13 && hour < 21) return "New York";
        if (hour >= 0 && hour < 8) return "Tokyo/Sydney";
        return "Asian/Pacific";
    }

    private async Task<List<CandleData>> FetchCandlesAsync(string instrument, string granularity, int count)
    {
        var url = $"/v3/instruments/{instrument}/candles?count={count}&price=M&granularity={granularity}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
            return new List<CandleData>();

        var data = await response.Content.ReadFromJsonAsync<OandaCandlesResponse>();
        if (data?.Candles == null) return new List<CandleData>();

        return data.Candles
            .Where(c => c.Complete)
            .Select(c => new CandleData(
                decimal.Parse(c.Mid.O),
                decimal.Parse(c.Mid.H),
                decimal.Parse(c.Mid.L),
                decimal.Parse(c.Mid.C)
            ))
            .ToList();
    }

    private record CandleData(decimal Open, decimal High, decimal Low, decimal Close);
    private class OandaCandlesResponse
    {
        public List<OandaCandle> Candles { get; set; } = new();
    }
    private class OandaCandle
    {
        public bool Complete { get; set; }
        public OandaCandleMid Mid { get; set; } = new();
    }
    private class OandaCandleMid
    {
        public string O { get; set; } = "0";
        public string H { get; set; } = "0";
        public string L { get; set; } = "0";
        public string C { get; set; } = "0";
    }
}
