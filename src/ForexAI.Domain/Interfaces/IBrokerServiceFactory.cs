namespace ForexAI.Domain.Interfaces;

public interface IBrokerServiceFactory
{
    IMarketDataService GetMarketDataService(string brokerName);
    ITradeExecutionService GetExecutionService(string brokerName);
}
