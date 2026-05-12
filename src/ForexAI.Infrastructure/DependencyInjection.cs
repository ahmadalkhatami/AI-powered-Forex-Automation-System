using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure.Broker;
using ForexAI.Infrastructure.Mifx;
using ForexAI.Infrastructure.Persistence.Repositories;
using ForexAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddScoped<ITradePositionRepository, JsonTradePositionRepository>();
        services.AddScoped<ISignalRepository, JsonSignalRepository>();
        services.AddScoped<ISignalAnalyzer, LiveSignalAnalyzer>();
        services.AddScoped<IRiskEvaluator, RuleBasedRiskEvaluator>();

        // Yahoo Finance — typed client, shared untuk IMarketDataService + ICandleDataService
        services.AddHttpClient<YahooFinanceService>(c =>
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"));
        services.AddTransient<ICandleDataService>(sp => sp.GetRequiredService<YahooFinanceService>());

        // MIFX singletons — selalu didaftarkan agar MifxBridgeController bisa menerima tick
        services.AddSingleton<MifxPriceFeed>();
        services.AddSingleton<MifxCommandQueue>();

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
