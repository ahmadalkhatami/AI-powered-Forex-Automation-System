using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure.Broker;
using ForexAI.Infrastructure.Persistence.Repositories;
using ForexAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForexAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITradePositionRepository, JsonTradePositionRepository>();
        services.AddScoped<ISignalRepository, JsonSignalRepository>();
        services.AddScoped<ISignalAnalyzer, BmadSignalAnalyzer>();
        services.AddScoped<IRiskEvaluator, RuleBasedRiskEvaluator>();
        services.AddScoped<IMarketDataService, StubMarketDataService>();
        services.AddScoped<IBrokerService, NullBrokerService>();
        return services;
    }
}
