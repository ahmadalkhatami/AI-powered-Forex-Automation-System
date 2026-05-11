using System.Text.Json;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

public class BmadSignalAnalyzer : ISignalAnalyzer
{
    private readonly string _signalOutputPath;
    private readonly string _riskDecisionPath;

    public BmadSignalAnalyzer()
    {
        _signalOutputPath = Path.Combine(ProjectPaths.PlanningArtifactsDir, "signal-output.json");
        _riskDecisionPath = Path.Combine(ProjectPaths.PlanningArtifactsDir, "risk-decision.json");
    }

    public BmadSignalAnalyzer(string signalOutputPath, string riskDecisionPath)
    {
        _signalOutputPath = signalOutputPath;
        _riskDecisionPath = riskDecisionPath;
    }

    public async Task<TradeSignal> AnalyzeAsync(MarketSnapshot snapshot)
    {
        var signalJson = await File.ReadAllTextAsync(_signalOutputPath);
        var riskJson = await File.ReadAllTextAsync(_riskDecisionPath);

        using var signalDoc = JsonDocument.Parse(signalJson);
        using var riskDoc = JsonDocument.Parse(riskJson);

        var s = signalDoc.RootElement;
        var r = riskDoc.RootElement;

        var analysis = s.GetProperty("analysis");
        var trend = analysis.GetProperty("trend");
        var momentum = analysis.GetProperty("momentum");
        var structure = analysis.GetProperty("structure");
        var riskParams = r.GetProperty("trade_parameters");

        var trendAnalysis = new TrendAnalysis(
            Bias: trend.GetProperty("bias").GetString() ?? "",
            Strength: trend.GetProperty("strength").GetString() ?? "",
            Score: trend.GetProperty("score").GetDecimal(),
            HtfAligned: trend.GetProperty("htf_aligned").GetBoolean(),
            Configuration: trend.GetProperty("configuration").GetString() ?? "",
            ScoreRationale: trend.GetProperty("score_rationale").GetString() ?? "");

        var momentumAnalysis = new MomentumAnalysis(
            RSIValue: momentum.GetProperty("rsi_value").GetDecimal(),
            RSIDirection: momentum.GetProperty("direction").GetString() ?? "",
            Zone: momentum.GetProperty("zone").GetString() ?? "",
            Score: momentum.GetProperty("score").GetDecimal(),
            ScoreRationale: momentum.GetProperty("score_rationale").GetString() ?? "",
            Divergence: momentum.TryGetProperty("divergence", out var div) && div.ValueKind != JsonValueKind.Null
                ? div.GetString()
                : null);

        var structureAnalysis = new StructureAnalysis(
            NearestSupport: structure.GetProperty("nearest_support").GetDecimal(),
            NearestResistance: structure.GetProperty("nearest_resistance").GetDecimal(),
            Score: structure.GetProperty("score").GetDecimal(),
            ScoreRationale: structure.GetProperty("score_rationale").GetString() ?? "",
            CandleConfirmed: structure.GetProperty("candle_confirmation").GetBoolean(),
            CandlePattern: structure.GetProperty("candle_pattern").GetString() ?? "",
            PricePosition: structure.GetProperty("price_position").GetString() ?? "");

        var tradeParameters = new TradeParameters(
            Entry: riskParams.GetProperty("entry").GetDecimal(),
            StopLoss: riskParams.GetProperty("stop_loss").GetDecimal(),
            StopLossPips: riskParams.GetProperty("stop_loss_pips").GetInt32(),
            TakeProfit: riskParams.GetProperty("take_profit").GetDecimal(),
            TakeProfitPips: riskParams.GetProperty("take_profit_pips").GetInt32(),
            LotSize: riskParams.GetProperty("lot_size").GetDecimal(),
            RiskAmount: riskParams.GetProperty("risk_amount").GetDecimal(),
            PotentialProfit: riskParams.GetProperty("potential_profit").GetDecimal(),
            RiskRewardRatio: riskParams.GetProperty("risk_reward_ratio").GetDecimal());

        var warnings = new List<string>();
        if (s.TryGetProperty("warnings", out var warningsEl))
        {
            foreach (var w in warningsEl.EnumerateArray())
            {
                var text = w.GetString();
                if (text is not null)
                    warnings.Add(text);
            }
        }

        var signalStr = s.GetProperty("signal").GetString() ?? "HOLD";
        var signalDirection = Enum.Parse<SignalDirection>(signalStr, ignoreCase: true);

        return new TradeSignal(
            runId: s.GetProperty("run_id").GetString() ?? "",
            pair: s.GetProperty("pair").GetString() ?? "",
            timeframe: s.GetProperty("timeframe").GetString() ?? "",
            signal: signalDirection,
            confluenceScore: s.GetProperty("confluence_score").GetInt32(),
            confidenceScore: s.GetProperty("confidence_score").GetDecimal(),
            snapshot: snapshot,
            trend: trendAnalysis,
            momentum: momentumAnalysis,
            structure: structureAnalysis,
            parameters: tradeParameters,
            warnings: warnings.AsReadOnly());
    }
}
