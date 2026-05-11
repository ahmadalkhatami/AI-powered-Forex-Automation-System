using System.Text.Json;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

public class StubMarketDataService : IMarketDataService
{
    private readonly string _signalOutputPath;

    public StubMarketDataService()
    {
        _signalOutputPath = ResolveDefaultPath();
    }

    public StubMarketDataService(string signalOutputPath)
    {
        _signalOutputPath = signalOutputPath;
    }

    private static string ResolveDefaultPath() =>
        Path.Combine(ProjectPaths.PlanningArtifactsDir, "signal-output.json");

    public async Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        var json = await File.ReadAllTextAsync(_signalOutputPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ps = root.GetProperty("price_snapshot");

        return new MarketSnapshot(
            Pair: root.GetProperty("pair").GetString() ?? pair,
            Timeframe: root.GetProperty("timeframe").GetString() ?? timeframe,
            CurrentPrice: ps.GetProperty("current_price").GetDecimal(),
            MA20_M15: ps.GetProperty("ma_20_m15").GetDecimal(),
            MA50_M15: ps.GetProperty("ma_50_m15").GetDecimal(),
            MA20_H1: ps.GetProperty("ma_20_h1").GetDecimal(),
            MA50_H1: ps.GetProperty("ma_50_h1").GetDecimal(),
            RSI14: ps.GetProperty("rsi_14").GetDecimal(),
            RSIDirection: ps.GetProperty("rsi_direction").GetString() ?? "",
            SupportZone: ps.GetProperty("support_zone").GetString() ?? "",
            ResistanceZone: ps.GetProperty("resistance_zone_1").GetString() ?? "",
            Session: ps.GetProperty("session").GetString() ?? "",
            CapturedAt: DateTimeOffset.Parse(
                root.GetProperty("timestamp").GetString() ?? DateTimeOffset.UtcNow.ToString("O")));
    }
}
