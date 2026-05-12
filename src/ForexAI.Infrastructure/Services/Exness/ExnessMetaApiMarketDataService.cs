using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;

namespace ForexAI.Infrastructure.Services.Exness;

public class ExnessMetaApiMarketDataService : IMarketDataService
{
    private readonly HttpClient _httpClient;
    private readonly string _accountId;

    public ExnessMetaApiMarketDataService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        var baseUrl = configuration["BrokerIntegration:Exness:BaseUrl"] ?? "https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai";
        var token = configuration["BrokerIntegration:Exness:AuthToken"];
        _accountId = configuration["BrokerIntegration:Exness:AccountId"] ?? "";
        
        _httpClient.BaseAddress = new Uri(baseUrl);
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Add("auth-token", token);
        }
    }

    public async Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        string exnessPair = pair.Replace("/", ""); // EURUSD
        // MetaApi timeframes: 15m, 1h
        var m15Candles = await FetchCandlesAsync(exnessPair, "15m", 100);
        var h1Candles = await FetchCandlesAsync(exnessPair, "1h", 100);

        if (m15Candles.Count == 0 || h1Candles.Count == 0)
        {
            throw new Exception("Failed to fetch market data from Exness via MetaApi.");
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

    private async Task<List<CandleData>> FetchCandlesAsync(string symbol, string timeframe, int limit)
    {
        if (string.IsNullOrEmpty(_accountId)) return new List<CandleData>();

        var url = $"/users/current/accounts/{_accountId}/historical-market-data/symbols/{symbol}/candles?timeframe={timeframe}&limit={limit}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
            return new List<CandleData>();

        var candles = await response.Content.ReadFromJsonAsync<List<MetaApiCandle>>();
        if (candles == null) return new List<CandleData>();

        return candles
            .Select(c => new CandleData(
                c.Open,
                c.High,
                c.Low,
                c.Close
            ))
            .ToList();
    }

    private record CandleData(decimal Open, decimal High, decimal Low, decimal Close);
    private class MetaApiCandle
    {
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
    }
}
