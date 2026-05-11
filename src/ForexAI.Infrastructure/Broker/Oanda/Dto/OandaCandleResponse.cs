using System.Text.Json.Serialization;

namespace ForexAI.Infrastructure.Broker.Oanda.Dto;

public class OandaCandleResponse
{
    [JsonPropertyName("instrument")]
    public string Instrument { get; set; } = "";

    [JsonPropertyName("granularity")]
    public string Granularity { get; set; } = "";

    [JsonPropertyName("candles")]
    public List<OandaCandle> Candles { get; set; } = new();
}

public class OandaCandle
{
    [JsonPropertyName("time")]
    public string Time { get; set; } = "";

    [JsonPropertyName("mid")]
    public OandaCandleMid Mid { get; set; } = new();

    [JsonPropertyName("volume")]
    public int Volume { get; set; }

    [JsonPropertyName("complete")]
    public bool Complete { get; set; }
}

public class OandaCandleMid
{
    [JsonPropertyName("o")]
    public string Open { get; set; } = "";

    [JsonPropertyName("h")]
    public string High { get; set; } = "";

    [JsonPropertyName("l")]
    public string Low { get; set; } = "";

    [JsonPropertyName("c")]
    public string Close { get; set; } = "";
}
