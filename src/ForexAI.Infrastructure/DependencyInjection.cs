using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure.Broker;
using ForexAI.Infrastructure.Mifx;
using ForexAI.Infrastructure.Persistence.Repositories;
using ForexAI.Infrastructure.Services;
using ForexAI.Infrastructure.Services.Deriv;
using ForexAI.Infrastructure.Services.Exness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure;

public class MifxSettings
{
    public const string Section = "Mifx";
    public bool EnableExecution { get; set; } = true;
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var mifx = configuration.GetSection(MifxSettings.Section).Get<MifxSettings>() ?? new();

        // Mode service (HARUS dideklarasi sebelum repositories yang depend on it)
        services.AddSingleton<IModeService, ModeService>();

        // Singletons for cross-cutting safety + audit
        services.AddSingleton<ISystemStateService, SystemStateService>();
        services.AddSingleton<IAdaptiveStateService, AdaptiveStateService>();
        services.AddSingleton<AuditLogger>();
        services.AddScoped<BacktestRunner>();

        services.AddScoped<EaDeployService>();
        services.AddScoped<ITradePositionRepository, JsonTradePositionRepository>();
        services.AddScoped<ISignalRepository, JsonSignalRepository>();
        services.AddScoped<ISignalAnalyzer, LiveSignalAnalyzer>();
        services.AddScoped<IRiskEvaluator, RuleBasedRiskEvaluator>();

        // MT5 bridge — bus untuk Exness MT5 (legacy market data path)
        services.AddSingleton<Mt5CommandBus>();
        services.AddScoped<ExnessLocalMt5MarketDataService>();
        services.AddScoped<ExnessLocalMt5ExecutionService>();
        services.AddHttpClient<ExnessMetaApiMarketDataService>();
        services.AddHttpClient<ExnessMetaApiExecutionService>();
        services.AddSingleton(sp =>
        {
            var token = configuration["BrokerIntegration:Deriv:ApiToken"] ?? "";
            var appId = configuration["BrokerIntegration:Deriv:AppId"] ?? DerivWebSocketClient.DefaultDemoAppId;
            var logger = sp.GetRequiredService<ILogger<DerivWebSocketClient>>();
            return new DerivWebSocketClient(token, appId, logger);
        });
        services.AddScoped<DerivMarketDataService>();
        services.AddScoped<DerivExecutionService>();

        // MIFX singletons — ingestion EA: tick + candle + command queue + position sync
        services.AddSingleton<MifxPriceFeed>();
        // MifxPriceFeed implements IMarketSpreadGate — same instance untuk spread veto
        services.AddSingleton<IMarketSpreadGate>(sp => sp.GetRequiredService<MifxPriceFeed>());
        services.AddSingleton<MifxCandleFeed>();
        services.AddSingleton<MifxCommandQueue>();
        services.AddScoped<MifxPositionSyncService>();

        // IMarketDataService: selalu dari MIFX EA (data lengkap MA/RSI/S-R via tick payload)
        services.AddTransient<IMarketDataService, MifxFullDataService>();

        // ICandleDataService: candle M15/H1/D1 dari MIFX EA push (via MifxCandleFeed)
        services.AddTransient<ICandleDataService, MifxCandleDataService>();

        // IBrokerService: MIFX via EA atau null (simulasi)
        if (mifx.EnableExecution)
            services.AddScoped<IBrokerService, MifxBrokerService>();
        else
            services.AddScoped<IBrokerService, NullBrokerService>();

        return services;
    }
}
