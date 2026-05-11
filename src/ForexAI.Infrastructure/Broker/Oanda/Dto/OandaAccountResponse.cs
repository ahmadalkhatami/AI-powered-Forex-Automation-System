using System.Text.Json.Serialization;

namespace ForexAI.Infrastructure.Broker.Oanda.Dto;

public class OandaAccountSummaryResponse
{
    [JsonPropertyName("account")]
    public OandaAccountData Account { get; set; } = new();
}

public class OandaAccountData
{
    [JsonPropertyName("balance")]
    public string Balance { get; set; } = "0";

    [JsonPropertyName("NAV")]
    public string Nav { get; set; } = "0";

    [JsonPropertyName("unrealizedPL")]
    public string UnrealizedPl { get; set; } = "0";
}
