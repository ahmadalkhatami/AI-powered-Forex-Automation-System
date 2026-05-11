namespace ForexAI.Infrastructure.Broker.Oanda;

public class OandaSettings
{
    public const string Section = "Oanda";

    public string Environment { get; set; } = "Practice";
    public string AccountId { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public bool EnableLiveData { get; set; } = false;
    public bool EnableExecution { get; set; } = false;

    public string BaseUrl => Environment == "Live"
        ? "https://api-fxtrade.oanda.com"
        : "https://api-fxpractice.oanda.com";
}
