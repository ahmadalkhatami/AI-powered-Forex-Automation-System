using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure.Broker;
using ForexAI.Infrastructure.Mifx;
using ForexAI.Infrastructure.Persistence.Repositories;
using ForexAI.Infrastructure.Services;
using ForexAI.Infrastructure.Services.Deriv;
using ForexAI.Infrastructure.Services.Exness;
using ForexAI.Infrastructure.Services.Oanda;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure;

public class MifxSettings
{
    public const string Section = "Mifx";
    public bool EnableLivePrice { get; set; } = false;
    public bool EnableExecution  { get; set; } = false;
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var mifx = configuration.GetSection(MifxSettings.Section).Get<MifxSettings>() ?? new();

        services.AddScoped<EaDeployService>();
        services.AddScoped<ITradePositionRepository, JsonTradePositionRepository>();
        services.AddScoped<ISignalRepository, JsonSignalRepository>();
        services.AddScoped<ISignalAnalyzer, LiveSignalAnalyzer>();
        services.AddScoped<IRiskEvaluator, RuleBasedRiskEvaluator>();
        services.AddScoped<IBrokerServiceFactory, BrokerServiceFactory>();

        // Yahoo Finance — typed client, shared untuk IMarketDataService + ICandleDataService
        services.AddHttpClient<YahooFinanceService>(c =>
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"));
        services.AddTransient<ICandleDataService>(sp => sp.GetRequiredService<YahooFinanceService>());

        // Multi-broker services used by BrokerServiceFactory and the MT5 bridge.
        services.AddSingleton<Mt5CommandBus>();
        services.AddScoped<ExnessLocalMt5MarketDataService>();
        services.AddScoped<ExnessLocalMt5ExecutionService>();
        services.AddHttpClient<ExnessMetaApiMarketDataService>();
        services.AddHttpClient<ExnessMetaApiExecutionService>();
        services.AddHttpClient<OandaMarketDataService>();
        services.AddHttpClient<OandaExecutionService>();
        services.AddSingleton(sp =>
        {
            var token = configuration["BrokerIntegration:Deriv:ApiToken"] ?? "";
            var appId = configuration["BrokerIntegration:Deriv:AppId"] ?? DerivWebSocketClient.DefaultDemoAppId;
            var logger = sp.GetRequiredService<ILogger<DerivWebSocketClient>>();
            return new DerivWebSocketClient(token, appId, logger);
        });
        services.AddScoped<DerivMarketDataService>();
        services.AddScoped<DerivExecutionService>();

        // MIFX singletons — selalu didaftarkan agar MifxBridgeController bisa menerima tick
        services.AddSingleton<MifxPriceFeed>();
        services.AddSingleton<MifxCommandQueue>();
        services.AddScoped<MifxPositionSyncService>();

        // IMarketDataService: MT5 full data (tanpa Yahoo) atau Yahoo saja (simulasi)
        if (mifx.EnableLivePrice)
            services.AddTransient<IMarketDataService, MifxFullDataService>();
        else
            services.AddTransient<IMarketDataService>(sp => sp.GetRequiredService<YahooFinanceService>());

        // IBrokerService: MIFX via EA atau null (simulasi)
        if (mifx.EnableExecution)
            services.AddScoped<IBrokerService, MifxBrokerService>();
        else
            services.AddScoped<IBrokerService, NullBrokerService>();

        return services;
    }
}
