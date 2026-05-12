using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure.Services.Exness;
using ForexAI.Infrastructure.Services.Oanda;
using ForexAI.Infrastructure.Services.Deriv;
using Microsoft.Extensions.DependencyInjection;

namespace ForexAI.Infrastructure.Services;

public class BrokerServiceFactory : IBrokerServiceFactory
{
    private readonly IServiceProvider _serviceProvider;

    public BrokerServiceFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IMarketDataService GetMarketDataService(string brokerName)
    {
        return (brokerName?.ToUpper() ?? "OANDA") switch
        {
            "EXNESS" => _serviceProvider.GetRequiredService<ExnessLocalMt5MarketDataService>(),
            "DERIV" => _serviceProvider.GetRequiredService<DerivMarketDataService>(),
            "STUB" => _serviceProvider.GetRequiredService<StubMarketDataService>(),
            _ => _serviceProvider.GetRequiredService<OandaMarketDataService>()
        };
    }

    public ITradeExecutionService GetExecutionService(string brokerName)
    {
        return (brokerName?.ToUpper() ?? "OANDA") switch
        {
            "EXNESS" => _serviceProvider.GetRequiredService<ExnessLocalMt5ExecutionService>(),
            "DERIV" => _serviceProvider.GetRequiredService<DerivExecutionService>(),
            _ => _serviceProvider.GetRequiredService<OandaExecutionService>()
        };
    }
}
