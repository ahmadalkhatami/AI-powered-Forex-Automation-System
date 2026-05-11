using System.Text.Json.Serialization;

namespace ForexAI.Infrastructure.Broker.Oanda.Dto;

public class OandaOrderResponse
{
    [JsonPropertyName("orderFillTransaction")]
    public OandaOrderFillTransaction? OrderFillTransaction { get; set; }

    [JsonPropertyName("orderCancelTransaction")]
    public OandaOrderCancelTransaction? OrderCancelTransaction { get; set; }
}

public class OandaOrderFillTransaction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("tradeOpened")]
    public OandaTradeOpened? TradeOpened { get; set; }
}

public class OandaTradeOpened
{
    [JsonPropertyName("tradeID")]
    public string TradeId { get; set; } = "";

    [JsonPropertyName("price")]
    public string Price { get; set; } = "";

    [JsonPropertyName("units")]
    public string Units { get; set; } = "";
}

public class OandaOrderCancelTransaction
{
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}
