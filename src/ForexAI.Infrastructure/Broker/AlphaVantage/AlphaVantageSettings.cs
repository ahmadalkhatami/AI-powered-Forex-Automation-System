namespace ForexAI.Infrastructure.Broker.AlphaVantage;

public class AlphaVantageSettings
{
    public const string Section = "AlphaVantage";
    public string ApiKey { get; set; } = "";
    public bool EnableLiveData { get; set; } = false;
    public string BaseUrl => "https://www.alphavantage.co";
}
