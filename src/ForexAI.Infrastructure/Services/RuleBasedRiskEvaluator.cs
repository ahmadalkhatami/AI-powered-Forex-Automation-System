using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

/// <summary>
/// Risk gate berbasis rules: validasi sinyal terhadap confidence minimum, max open positions,
/// dan <b>daily risk cap berbasis tier equity</b>.
///
/// <para>Daily cap melindungi modal kecil ($50) dari blow-up di hari sial:
/// 3 trade losing streak di 2% risk × 3 = 6% drawdown sehari = hard stop sampai besok.</para>
/// </summary>
public class RuleBasedRiskEvaluator : IRiskEvaluator
{
    private const decimal MinConfidence  = 0.0m;   // TEST: buka semua confidence
    private const decimal HighConfidence = 0.60m;  // TEST: caution mulai 60%
    private const int     MaxOpenPositions = 3;

    private readonly IModeService _mode;

    public RuleBasedRiskEvaluator(IModeService mode)
    {
        _mode = mode;
    }

    public Task<RiskValidation> EvaluateAsync(
        TradeSignal     signal,
        PredictorResult predictor,
        decimal         equity,
        int             openPositions,
        DailyRiskUsage  dailyUsage)
    {
        var tier = RiskTier.FromEquity(equity, _mode.CurrentMode);
        var noGoReasons  = new List<string>();
        var cautionNotes = new List<string>();

        // ── Hard gates: confidence, max positions ──────────────────────────
        // TEST: HOLD di-skip agar bisa approve manual untuk testing
        // if (signal.Signal == SignalDirection.HOLD)
        //     noGoReasons.Add("Signal is HOLD — no actionable direction");

        if (predictor.AdjustedConfidence < MinConfidence)
            noGoReasons.Add(
                $"AI confidence {predictor.AdjustedConfidence:P0} below {MinConfidence:P0} minimum");

        if (openPositions >= MaxOpenPositions)
            noGoReasons.Add($"Max open positions reached ({openPositions}/{MaxOpenPositions})");

        // ── Daily risk cap (tier-aware) ─────────────────────────────────────
        // Hitung exposure prospektif: trade hari ini + trade yang akan dibuka
        decimal prospectiveRiskUsd  = dailyUsage.UsedUsd + signal.Parameters.RiskAmount;
        decimal prospectivePct      = equity > 0 ? prospectiveRiskUsd / equity : 0m;
        decimal dailyCapUsd         = equity * tier.DailyCapPct;

        if (dailyUsage.TradeCount >= tier.MaxDailyTrades)
            noGoReasons.Add(
                $"Max daily trades reached ({dailyUsage.TradeCount}/{tier.MaxDailyTrades}) — " +
                $"tier '{tier.Name}'. Sistem auto-resume di UTC midnight.");

        if (prospectivePct > tier.DailyCapPct)
            noGoReasons.Add(
                $"Daily risk cap akan terlewat: " +
                $"{prospectivePct:P1} > {tier.DailyCapPct:P0} (cap tier '{tier.Name}'). " +
                $"Sudah dipakai ${dailyUsage.UsedUsd:F2} dari ${dailyCapUsd:F2}.");

        // Caution: mendekati cap (>80% utilization sebelum trade ini)
        decimal currentUtilization = equity > 0 ? dailyUsage.UsedUsd / (equity * tier.DailyCapPct) : 0m;
        if (currentUtilization >= 0.80m && noGoReasons.Count == 0)
            cautionNotes.Add(
                $"Daily risk utilisasi tinggi ({currentUtilization:P0} dari cap) — " +
                $"setelah trade ini, ruang untuk trade tambahan minim.");

        if (noGoReasons.Count > 0)
            return Task.FromResult(new RiskValidation(
                "NO-GO", PositionDecision.REJECT, null,
                Array.Empty<string>(), noGoReasons));

        // ── Caution gates ───────────────────────────────────────────────────
        if (predictor.AdjustedConfidence < HighConfidence)
            cautionNotes.Add(
                $"Confidence {predictor.AdjustedConfidence:P0} valid tapi di bawah {HighConfidence:P0} — trade with caution");

        var decision = cautionNotes.Count > 0 ? "GO_WITH_CAUTION" : "GO";

        return Task.FromResult(new RiskValidation(
            decision, PositionDecision.OPEN,
            signal.Parameters, cautionNotes, Array.Empty<string>()));
    }
}
