using System.Net.Http.Headers;
using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure.Broker.Oanda;
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
        var oanda = configuration.GetSection(OandaSettings.Section).Get<OandaSettings>() ?? new OandaSettings();
        services.Configure<OandaSettings>(configuration.GetSection(OandaSettings.Section));

        services.AddScoped<ITradePositionRepository, JsonTradePositionRepository>();
        services.AddScoped<ISignalRepository, JsonSignalRepository>();
        services.AddScoped<ISignalAnalyzer, BmadSignalAnalyzer>();
        services.AddScoped<IRiskEvaluator, RuleBasedRiskEvaluator>();

        if (oanda.EnableLiveData && !string.IsNullOrWhiteSpace(oanda.ApiToken))
        {
            services.AddHttpClient<IMarketDataService, OandaMarketDataService>(
                client => ConfigureOandaClient(client, oanda));
        }
        else
        {
            services.AddScoped<IMarketDataService, StubMarketDataService>();
        }

        if (oanda.EnableExecution && !string.IsNullOrWhiteSpace(oanda.ApiToken))
        {
            services.AddHttpClient<IBrokerService, OandaBrokerService>(
                client => ConfigureOandaClient(client, oanda));
        }
        else
        {
            services.AddScoped<IBrokerService, NullBrokerService>();
        }

        return services;
    }

    private static void ConfigureOandaClient(HttpClient client, OandaSettings oanda)
    {
        client.BaseAddress = new Uri(oanda.BaseUrl);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", oanda.ApiToken);
    }
}
