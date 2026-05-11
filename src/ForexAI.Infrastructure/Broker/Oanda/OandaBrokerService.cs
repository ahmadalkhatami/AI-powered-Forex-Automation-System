using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure.Broker.Oanda.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForexAI.Infrastructure.Broker.Oanda;

public class OandaBrokerService : IBrokerService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly OandaSettings _settings;
    private readonly ILogger<OandaBrokerService> _logger;

    public bool IsLive => true;

    public OandaBrokerService(
        HttpClient http,
        IOptions<OandaSettings> settings,
        ILogger<OandaBrokerService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<BrokerAccountInfo> GetAccountAsync()
    {
        var url = $"/v3/accounts/{_settings.AccountId}/summary";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OandaAccountSummaryResponse>(body, JsonOpts)
            ?? throw new InvalidOperationException("Empty account summary response");

        var balance = decimal.Parse(result.Account.Balance);
        var equity = decimal.Parse(result.Account.Nav);
        var unrealized = decimal.Parse(result.Account.UnrealizedPl);

        _logger.LogInformation(
            "OANDA account — Balance: {Balance}, Equity (NAV): {Equity}, Unrealized P&L: {Unrealized}",
            balance, equity, unrealized);

        return new BrokerAccountInfo(balance, equity, unrealized);
    }

    public async Task<string?> PlaceOrderAsync(BrokerOrderRequest request)
    {
        var instrument = ToOandaInstrument(request.Instrument);
        var units = (long)(request.LotSize * 100_000);
        if (!request.IsBuy) units = -units;

        var orderBody = new
        {
            order = new
            {
                type = "MARKET",
                instrument,
                units = units.ToString(),
                timeInForce = "FOK",
                stopLossOnFill = new { price = request.StopLoss.ToString("F5") },
                takeProfitOnFill = new { price = request.TakeProfit.ToString("F5") }
            }
        };

        var json = JsonSerializer.Serialize(orderBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = $"/v3/accounts/{_settings.AccountId}/orders";

        _logger.LogInformation(
            "Placing OANDA order — {Direction} {Instrument} {Units} units, SL={SL}, TP={TP}",
            request.IsBuy ? "BUY" : "SELL", instrument, units,
            request.StopLoss.ToString("F5"), request.TakeProfit.ToString("F5"));

        var response = await _http.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OANDA order failed ({Status}): {Body}", response.StatusCode, responseBody);
            response.EnsureSuccessStatusCode();
        }

        var result = JsonSerializer.Deserialize<OandaOrderResponse>(responseBody, JsonOpts);

        if (result?.OrderCancelTransaction != null)
        {
            _logger.LogWarning("OANDA order cancelled: {Reason}", result.OrderCancelTransaction.Reason);
            return null;
        }

        var tradeId = result?.OrderFillTransaction?.TradeOpened?.TradeId;
        _logger.LogInformation("OANDA order filled — ExternalTradeId: {TradeId}", tradeId);
        return tradeId;
    }

    private static string ToOandaInstrument(string pair)
    {
        pair = pair.Replace("/", "").ToUpperInvariant();
        return pair.Length == 6 ? pair[..3] + "_" + pair[3..] : pair;
    }
}
