using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure.Persistence.Repositories;
using ForexAI.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ForexAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ITradePositionRepository, JsonTradePositionRepository>();
        services.AddScoped<ISignalRepository, JsonSignalRepository>();
        services.AddScoped<IMarketDataService, StubMarketDataService>();
        services.AddScoped<ISignalAnalyzer, BmadSignalAnalyzer>();
        services.AddScoped<IRiskEvaluator, RuleBasedRiskEvaluator>();
        return services;
    }
}
