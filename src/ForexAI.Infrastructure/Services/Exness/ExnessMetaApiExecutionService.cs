using System.Net.Http.Json;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace ForexAI.Infrastructure.Services.Exness;

public class ExnessMetaApiExecutionService : ITradeExecutionService
{
    private readonly HttpClient _httpClient;
    private readonly string _accountId;

    public ExnessMetaApiExecutionService(HttpClient httpClient, IConfiguration configuration)
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

    public async Task<BrokerAccountStatus> GetAccountStatusAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_accountId))
        {
            return new BrokerAccountStatus("EXNESS-MISSING", 10000m, 10000m, 0m, 10000m, 0); // fallback
        }

        var url = $"/users/current/accounts/{_accountId}/account-information";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to fetch Exness account info: {response.ReasonPhrase}");
        }

        var info = await response.Content.ReadFromJsonAsync<MetaApiAccountInfo>(cancellationToken: cancellationToken);
        
        // Count open positions
        var posUrl = $"/users/current/accounts/{_accountId}/positions";
        var posResp = await _httpClient.GetAsync(posUrl, cancellationToken);
        int openCount = 0;
        if (posResp.IsSuccessStatusCode)
        {
            var positions = await posResp.Content.ReadFromJsonAsync<List<object>>(cancellationToken: cancellationToken);
            openCount = positions?.Count ?? 0;
        }

        return new BrokerAccountStatus(
            _accountId,
            info?.Equity ?? 10000m,
            info?.Balance ?? 10000m,
            info?.Margin ?? 0m,
            (info?.Equity ?? 10000m) - (info?.Margin ?? 0m),
            openCount
        );
    }

    public async Task<BrokerExecutionResult> ExecuteOrderAsync(string pair, string direction, decimal lotSize, decimal? stopLoss, decimal? takeProfit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_accountId))
        {
            return new BrokerExecutionResult(false, null, "Exness AccountId missing", 0m);
        }

        string symbol = pair.Replace("/", ""); // EURUSD
        string actionType = direction.ToUpper() == "BUY" ? "ORDER_TYPE_BUY" : "ORDER_TYPE_SELL";

        var payload = new
        {
            actionType = actionType,
            symbol = symbol,
            volume = lotSize,
            stopLoss = stopLoss,
            takeProfit = takeProfit
        };

        var url = $"/users/current/accounts/{_accountId}/trade";
        var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            return new BrokerExecutionResult(false, null, err, 0m);
        }

        var result = await response.Content.ReadFromJsonAsync<MetaApiTradeResult>(cancellationToken: cancellationToken);
        return new BrokerExecutionResult(true, result?.OrderId ?? $"EX-{DateTime.UtcNow.Ticks}", null, result?.Price ?? 0m);
    }

    private class MetaApiAccountInfo
    {
        public decimal Equity { get; set; }
        public decimal Balance { get; set; }
        public decimal Margin { get; set; }
    }

    private class MetaApiTradeResult
    {
        public string OrderId { get; set; } = "";
        public decimal Price { get; set; }
    }
}
