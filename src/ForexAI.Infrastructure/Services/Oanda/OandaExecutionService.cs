using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services.Oanda;

public class OandaExecutionService : ITradeExecutionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OandaExecutionService> _logger;
    private readonly string _accountId;

    public OandaExecutionService(HttpClient httpClient, IConfiguration configuration, ILogger<OandaExecutionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        var baseUrl = configuration["BrokerIntegration:Oanda:BaseUrl"] ?? "https://api-fxpractice.oanda.com";
        _accountId = configuration["BrokerIntegration:Oanda:AccountId"] ?? "";
        var token = configuration["BrokerIntegration:Oanda:BearerToken"] ?? "";

        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<BrokerAccountStatus> GetAccountStatusAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_accountId))
        {
            _logger.LogWarning("OANDA AccountId is not configured. Returning mock status.");
            return new BrokerAccountStatus("MOCK", 10000m, 10000m, 0m, 10000m, 0);
        }

        var response = await _httpClient.GetAsync($"/v3/accounts/{_accountId}/summary", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to fetch OANDA account status: {Status} {Error}", response.StatusCode, err);
            throw new Exception($"Failed to fetch account status. OANDA returned {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);
        var account = document.RootElement.GetProperty("account");

        var equity = decimal.Parse(account.GetProperty("NAV").GetString() ?? "0");
        var balance = decimal.Parse(account.GetProperty("balance").GetString() ?? "0");
        var marginUsed = decimal.Parse(account.GetProperty("marginUsed").GetString() ?? "0");
        var marginAvailable = decimal.Parse(account.GetProperty("marginAvailable").GetString() ?? "0");
        var openPositionCount = account.GetProperty("openPositionCount").GetInt32();

        return new BrokerAccountStatus(_accountId, equity, balance, marginUsed, marginAvailable, openPositionCount);
    }

    public async Task<BrokerExecutionResult> ExecuteOrderAsync(
        string pair, 
        string direction, 
        decimal lotSize, 
        decimal? stopLoss, 
        decimal? takeProfit, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_accountId))
        {
            _logger.LogWarning("OANDA AccountId is not configured. Returning mock execution result.");
            return new BrokerExecutionResult(true, $"MOCK-{Guid.NewGuid().ToString()[..8]}", null, 0);
        }

        // Oanda uses standard pairs like "EUR_USD". Convert "EURUSD" to "EUR_USD".
        var instrument = pair.Length == 6 ? $"{pair[..3]}_{pair[3..]}" : pair;
        
        // Units in OANDA: positive for BUY, negative for SELL
        // lotSize in our system is usually standard lots (1 = 100,000 units).
        var units = (int)(lotSize * 100000);
        if (direction == "SELL")
        {
            units = -units;
        }

        var orderPayload = new Dictionary<string, object>
        {
            ["order"] = new Dictionary<string, object>
            {
                ["units"] = units.ToString(),
                ["instrument"] = instrument,
                ["timeInForce"] = "FOK",
                ["type"] = "MARKET",
                ["positionFill"] = "DEFAULT"
            }
        };

        var orderDict = (Dictionary<string, object>)orderPayload["order"];

        if (stopLoss.HasValue)
        {
            orderDict["stopLossOnFill"] = new { price = stopLoss.Value.ToString("F5") };
        }
        
        if (takeProfit.HasValue)
        {
            orderDict["takeProfitOnFill"] = new { price = takeProfit.Value.ToString("F5") };
        }

        var jsonPayload = JsonSerializer.Serialize(orderPayload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"/v3/accounts/{_accountId}/orders", content, cancellationToken);
        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OANDA Order Failed: {Status} {Response}", response.StatusCode, responseString);
            return new BrokerExecutionResult(false, null, responseString, 0);
        }

        using var doc = JsonDocument.Parse(responseString);
        var root = doc.RootElement;
        
        string? tradeId = null;
        decimal price = 0;

        if (root.TryGetProperty("orderFillTransaction", out var fillTx))
        {
            tradeId = fillTx.GetProperty("id").GetString();
            price = decimal.Parse(fillTx.GetProperty("price").GetString() ?? "0");
        }

        return new BrokerExecutionResult(true, tradeId, null, price);
    }
}
