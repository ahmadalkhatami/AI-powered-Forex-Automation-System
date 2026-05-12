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
        var riskJson   = await File.ReadAllTextAsync(_riskDecisionPath);

        using var signalDoc = JsonDocument.Parse(signalJson);
        using var riskDoc   = JsonDocument.Parse(riskJson);

        var s = signalDoc.RootElement;
        var r = riskDoc.RootElement;

        var analysis   = s.GetProperty("analysis");
        var trend      = analysis.GetProperty("trend");
        var momentum   = analysis.GetProperty("momentum");
        var structure  = analysis.GetProperty("structure");
        var riskParams = r.GetProperty("trade_parameters");

        // ── Signal direction (needed before parameter recalculation) ──────────
        var signalStr       = s.GetProperty("signal").GetString() ?? "HOLD";
        var signalDirection = Enum.Parse<SignalDirection>(signalStr, ignoreCase: true);

        // ── Trend: strategic bias from BMAD (text rationale stays) ───────────
        var trendAnalysis = new TrendAnalysis(
            Bias:           trend.GetProperty("bias").GetString() ?? "",
            Strength:       trend.GetProperty("strength").GetString() ?? "",
            Score:          trend.GetProperty("score").GetDecimal(),
            HtfAligned:     trend.GetProperty("htf_aligned").GetBoolean(),
            Configuration:  trend.GetProperty("configuration").GetString() ?? "",
            ScoreRationale: trend.GetProperty("score_rationale").GetString() ?? "");

        // ── Momentum: RSI value + direction from Yahoo Finance (live) ─────────
        var rsiZone = DeriveRsiZone(snapshot.RSI14);
        var momentumAnalysis = new MomentumAnalysis(
            RSIValue:       snapshot.RSI14,
            RSIDirection:   snapshot.RSIDirection,
            Zone:           rsiZone,
            Score:          momentum.GetProperty("score").GetDecimal(),
            ScoreRationale: momentum.GetProperty("score_rationale").GetString() ?? "",
            Divergence:     momentum.TryGetProperty("divergence", out var div) && div.ValueKind != JsonValueKind.Null
                                ? div.GetString()
                                : null);

        // ── Structure: support/resistance from Yahoo Finance (live) ──────────
        var liveSupport    = decimal.Parse(snapshot.SupportZone);
        var liveResistance = decimal.Parse(snapshot.ResistanceZone);
        var pricePosition  = DerivePricePosition(snapshot.CurrentPrice, liveSupport, liveResistance);

        var structureAnalysis = new StructureAnalysis(
            NearestSupport:     liveSupport,
            NearestResistance:  liveResistance,
            Score:              structure.GetProperty("score").GetDecimal(),
            ScoreRationale:     structure.GetProperty("score_rationale").GetString() ?? "",
            CandleConfirmed:    structure.GetProperty("candle_confirmation").GetBoolean(),
            CandlePattern:      structure.GetProperty("candle_pattern").GetString() ?? "",
            PricePosition:      pricePosition);

        // ── Trade parameters: BMAD pip distances applied to live price ────────
        var slPips     = riskParams.GetProperty("stop_loss_pips").GetInt32();
        var tpPips     = riskParams.GetProperty("take_profit_pips").GetInt32();
        var lotSize    = riskParams.GetProperty("lot_size").GetDecimal();
        var riskAmount = riskParams.GetProperty("risk_amount").GetDecimal();
        var riskReward = riskParams.GetProperty("risk_reward_ratio").GetDecimal();

        var entry = snapshot.CurrentPrice;
        decimal stopLoss, takeProfit;
        if (signalDirection == SignalDirection.SELL)
        {
            stopLoss   = entry + slPips * 0.0001m;
            takeProfit = entry - tpPips * 0.0001m;
        }
        else // BUY or HOLD → BUY geometry
        {
            stopLoss   = entry - slPips * 0.0001m;
            takeProfit = entry + tpPips * 0.0001m;
        }

        // potentialProfit = lot × tpPips × $10/pip (EURUSD standard lot)
        var potentialProfit = Math.Round(lotSize * tpPips * 10m, 2);

        var tradeParameters = new TradeParameters(
            Entry:           Math.Round(entry,      5),
            StopLoss:        Math.Round(stopLoss,   5),
            StopLossPips:    slPips,
            TakeProfit:      Math.Round(takeProfit, 5),
            TakeProfitPips:  tpPips,
            LotSize:         lotSize,
            RiskAmount:      riskAmount,
            PotentialProfit: potentialProfit,
            RiskRewardRatio: riskReward);

        // ── Warnings ─────────────────────────────────────────────────────────
        var warnings = new List<string>();
        if (s.TryGetProperty("warnings", out var warningsEl))
            foreach (var w in warningsEl.EnumerateArray())
            {
                var text = w.GetString();
                if (text is not null) warnings.Add(text);
            }

        // Add live-price note so user knows data is fresh
        warnings.Add($"Entry recalculated to live price {entry:F5} (Yahoo Finance). " +
                     $"BMAD pip distances preserved: SL {slPips}p / TP {tpPips}p.");

        return new TradeSignal(
            runId:           s.GetProperty("run_id").GetString() ?? "",
            pair:            s.GetProperty("pair").GetString() ?? "",
            timeframe:       s.GetProperty("timeframe").GetString() ?? "",
            signal:          signalDirection,
            confluenceScore: s.GetProperty("confluence_score").GetInt32(),
            confidenceScore: s.GetProperty("confidence_score").GetDecimal(),
            snapshot:        snapshot,
            trend:           trendAnalysis,
            momentum:        momentumAnalysis,
            structure:       structureAnalysis,
            parameters:      tradeParameters,
            warnings:        warnings.AsReadOnly());
    }

    private static string DeriveRsiZone(decimal rsi) => rsi switch
    {
        >= 70 => "Overbought",
        <= 30 => "Oversold",
        >= 55 => "Bullish",
        <= 45 => "Bearish",
        _     => "Neutral"
    };

    private static string DerivePricePosition(decimal price, decimal support, decimal resistance)
    {
        var range = resistance - support;
        if (range <= 0) return "Mid-range";
        var pct = (price - support) / range;
        return pct switch
        {
            >= 0.75m => "Near resistance",
            <= 0.25m => "Near support",
            _        => "Mid-range"
        };
    }
}
