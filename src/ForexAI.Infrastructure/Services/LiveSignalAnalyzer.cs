using System.Globalization;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

/// <summary>
/// Menganalisis sinyal trading secara real-time dari data pasar live (Yahoo Finance + MIFX).
/// Menggantikan BmadSignalAnalyzer yang membaca dari file JSON statis.
/// Semua score dihitung ulang setiap kali Trigger Analysis dipanggil.
/// </summary>
public class LiveSignalAnalyzer : ISignalAnalyzer
{
    private readonly IBrokerService _broker;

    public LiveSignalAnalyzer(IBrokerService broker)
    {
        _broker = broker;
    }

    public async Task<TradeSignal> AnalyzeAsync(MarketSnapshot snap)
    {
        // Ambil equity dari broker (live) atau fallback ke default
        decimal equity = 1_000m;
        try
        {
            var account = await _broker.GetAccountAsync();
            if (account.Balance > 0) equity = account.Balance;
        }
        catch { /* gunakan fallback */ }

        // ── 1. Trend ────────────────────────────────────────────────────────
        var (trend, bullishBias) = AnalyzeTrend(snap);

        // ── 2. Momentum ─────────────────────────────────────────────────────
        var momentum = AnalyzeMomentum(snap);

        // ── 3. Structure ────────────────────────────────────────────────────
        var (structure, pctFromSupport) = AnalyzeStructure(snap);

        // ── 4. Signal direction ─────────────────────────────────────────────
        var signal = DetermineSignal(bullishBias, momentum, pctFromSupport);

        // ── 5. Scores ───────────────────────────────────────────────────────
        var (confluenceScore, confidenceScore) = CalculateScores(trend, momentum, structure, signal);

        // ── 6. Trade parameters (1% risk rule) ─────────────────────────────
        var parameters = CalculateParameters(snap, signal, equity);

        // ── 7. Warnings ─────────────────────────────────────────────────────
        var warnings = BuildWarnings(snap, signal, confidenceScore, trend, momentum);

        return new TradeSignal(
            runId:           Guid.NewGuid().ToString("N")[..12],
            pair:            snap.Pair,
            timeframe:       snap.Timeframe,
            signal:          signal,
            confluenceScore: confluenceScore,
            confidenceScore: confidenceScore,
            snapshot:        snap,
            trend:           trend,
            momentum:        momentum,
            structure:       structure,
            parameters:      parameters,
            warnings:        warnings.AsReadOnly());
    }

    // ── Trend Analysis ────────────────────────────────────────────────────────
    private static (TrendAnalysis result, decimal bullishBias) AnalyzeTrend(MarketSnapshot snap)
    {
        bool priceAboveMA20  = snap.CurrentPrice > snap.MA20_M15;
        bool ma20AboveMA50   = snap.MA20_M15     > snap.MA50_M15;
        bool h1MA20AboveMA50 = snap.MA20_H1      > snap.MA50_H1;

        // Setiap kondisi bullish = +1 poin dari 3
        decimal bullishPoints = (priceAboveMA20 ? 1m : 0m)
                              + (ma20AboveMA50   ? 1m : 0m)
                              + (h1MA20AboveMA50 ? 1m : 0m);
        decimal bullishBias = bullishPoints / 3m; // 0=bear, 1=bull

        // Score = seberapa KONSISTEN semua indikator (0 = campur aduk, 1 = semua setuju)
        decimal trendScore = Math.Abs(bullishBias - 0.5m) * 2m;

        string bias     = bullishBias >= 0.6m ? "Bullish" : bullishBias <= 0.4m ? "Bearish" : "Neutral";
        string strength = trendScore switch { >= 0.8m => "Kuat", >= 0.5m => "Sedang", _ => "Lemah" };
        bool htfAligned = (bullishBias >= 0.5m) == h1MA20AboveMA50;

        string config = $"M15: MA20={snap.MA20_M15:F5} MA50={snap.MA50_M15:F5} | H1: MA20={snap.MA20_H1:F5} MA50={snap.MA50_H1:F5}";

        string rationale =
            $"Price {(priceAboveMA20 ? "di atas" : "di bawah")} MA20 M15; " +
            $"MA20 {(ma20AboveMA50 ? ">" : "<")} MA50 M15 ({(ma20AboveMA50 ? "bullish" : "bearish")}); " +
            $"H1 {(h1MA20AboveMA50 ? "bullish" : "bearish")} — HTF {(htfAligned ? "sejajar" : "berlawanan")}";

        return (new TrendAnalysis(
            Bias:           bias,
            Strength:       strength,
            Score:          Math.Round(trendScore, 2),
            HtfAligned:     htfAligned,
            Configuration:  config,
            ScoreRationale: rationale), bullishBias);
    }

    // ── Momentum Analysis ─────────────────────────────────────────────────────
    private static MomentumAnalysis AnalyzeMomentum(MarketSnapshot snap)
    {
        decimal rsi     = snap.RSI14;
        bool isRising   = snap.RSIDirection.Equals("rising", StringComparison.OrdinalIgnoreCase);

        string zone;
        decimal score;

        if      (rsi >= 70) { zone = "Overbought"; score = 0.25m; }  // terlalu mahal untuk BUY
        else if (rsi >= 60) { zone = "Bullish";    score = 0.75m; }  // momentum bullish bagus
        else if (rsi >= 50) { zone = "Bullish";    score = 0.60m; }  // bullish lemah
        else if (rsi >= 40) { zone = "Bearish";    score = 0.60m; }  // bearish lemah
        else if (rsi >= 30) { zone = "Bearish";    score = 0.75m; }  // momentum bearish bagus
        else                { zone = "Oversold";   score = 0.25m; }  // terlalu murah untuk SELL

        // Bonus/penalti dari arah RSI
        if (isRising) score = Math.Min(score + 0.10m, 1.0m);
        else          score = Math.Max(score - 0.05m, 0.0m);

        string rationale =
            $"RSI14={rsi:F1} di zona {zone}, " +
            $"arah {(isRising ? "naik ↑" : "turun ↓")}. " +
            $"Score: {Math.Round(score, 2):F2}";

        return new MomentumAnalysis(
            RSIValue:       rsi,
            RSIDirection:   snap.RSIDirection,
            Zone:           zone,
            Score:          Math.Round(score, 2),
            ScoreRationale: rationale,
            Divergence:     null);
    }

    // ── Structure Analysis ────────────────────────────────────────────────────
    private static (StructureAnalysis result, decimal pctFromSupport) AnalyzeStructure(MarketSnapshot snap)
    {
        // InvariantCulture: karena locale Indonesia pakai '.' sebagai sep ribuan
        // sehingga "1.16562" di-parse jadi 116562 jika pakai CurrentCulture
        decimal.TryParse(snap.SupportZone,    NumberStyles.Float, CultureInfo.InvariantCulture, out var support);
        decimal.TryParse(snap.ResistanceZone, NumberStyles.Float, CultureInfo.InvariantCulture, out var resistance);

        // Fallback jika parse gagal
        if (support    <= 0) support    = snap.CurrentPrice * 0.990m;
        if (resistance <= 0) resistance = snap.CurrentPrice * 1.010m;
        if (resistance <= support) resistance = support + 0.0050m;

        decimal range           = resistance - support;
        decimal pctFromSupport  = Math.Clamp((snap.CurrentPrice - support) / range, 0m, 1m);

        // Score: tinggi kalau harga dekat support atau resistance (level kunci jelas)
        decimal score;
        string position;

        if      (pctFromSupport <= 0.20m) { score = 0.80m; position = "Near support";    }
        else if (pctFromSupport >= 0.80m) { score = 0.80m; position = "Near resistance"; }
        else if (pctFromSupport <= 0.35m || pctFromSupport >= 0.65m)
                                          { score = 0.60m; position = "Mid-range";       }
        else                              { score = 0.40m; position = "Mid-range";       }

        bool candleConfirmed = pctFromSupport <= 0.25m || pctFromSupport >= 0.75m;

        string rationale =
            $"Support: {support.ToString("F5", CultureInfo.InvariantCulture)} | " +
            $"Resistance: {resistance.ToString("F5", CultureInfo.InvariantCulture)} | " +
            $"Price {pctFromSupport:P0} dari range ({position})";

        return (new StructureAnalysis(
            NearestSupport:     Math.Round(support,    5),
            NearestResistance:  Math.Round(resistance, 5),
            Score:              Math.Round(score, 2),
            ScoreRationale:     rationale,
            CandleConfirmed:    candleConfirmed,
            CandlePattern:      candleConfirmed ? "Level konfirmasi" : "Dalam range",
            PricePosition:      position), pctFromSupport);
    }

    // ── Signal Direction ──────────────────────────────────────────────────────
    private static SignalDirection DetermineSignal(
        decimal bullishBias, MomentumAnalysis momentum, decimal pctFromSupport)
    {
        // Voting system: 3 kondisi, masing-masing 1 suara
        bool trendBullish     = bullishBias > 0.55m;
        bool momentumBullish  = momentum.Zone is "Bullish" && momentum.RSIValue < 70;
        bool structureBullish = pctFromSupport < 0.45m; // price closer to support = BUY setup

        int buyVotes  = (trendBullish ? 1 : 0) + (momentumBullish ? 1 : 0) + (structureBullish ? 1 : 0);
        int sellVotes = 3 - buyVotes;

        return buyVotes >= 2 ? SignalDirection.BUY
             : sellVotes >= 2 ? SignalDirection.SELL
             : SignalDirection.HOLD;
    }

    // ── Confluence & Confidence Scores ────────────────────────────────────────
    private static (int confluenceScore, decimal confidenceScore) CalculateScores(
        TrendAnalysis trend, MomentumAnalysis momentum, StructureAnalysis structure, SignalDirection signal)
    {
        // Confluence = weighted average
        var raw = (trend.Score * 0.40m + momentum.Score * 0.35m + structure.Score * 0.25m) * 100m;
        int confluenceScore = (int)Math.Round(Math.Clamp(raw, 0m, 100m));

        // Confidence = seberapa konsisten semua score (stddev rendah = confidence tinggi)
        var scores = new[] { trend.Score, momentum.Score, structure.Score };
        decimal mean = scores.Average();
        decimal variance = scores.Average(s => (s - mean) * (s - mean));
        decimal stdDev = (decimal)Math.Sqrt((double)variance);

        // StdDev rendah → scores setuju → confidence tinggi
        decimal confidenceScore = Math.Clamp(1.0m - stdDev * 2.5m, 0.35m, 0.95m);

        // Penalti untuk HOLD — signal lemah
        if (signal == SignalDirection.HOLD) confidenceScore = Math.Min(confidenceScore, 0.62m);

        return (confluenceScore, Math.Round(confidenceScore, 2));
    }

    // ── Trade Parameters (1% Risk Rule) ───────────────────────────────────────
    private static TradeParameters CalculateParameters(
        MarketSnapshot snap, SignalDirection signal, decimal equity)
    {
        decimal riskAmount = Math.Round(equity * 0.01m, 2);  // 1% equity
        decimal entry      = snap.CurrentPrice;

        decimal.TryParse(snap.SupportZone,    NumberStyles.Float, CultureInfo.InvariantCulture, out var support);
        decimal.TryParse(snap.ResistanceZone, NumberStyles.Float, CultureInfo.InvariantCulture, out var resistance);
        if (support    <= 0) support    = entry * 0.990m;
        if (resistance <= 0) resistance = entry * 1.010m;

        decimal stopLoss, takeProfit;
        int slPips, tpPips;

        if (signal == SignalDirection.BUY)
        {
            // SL di bawah support, TP di resistance
            var rawSl = Math.Max(entry - support, 0.0010m);
            slPips    = (int)Math.Clamp(Math.Round(rawSl / 0.0001m), 10, 60);
            tpPips    = (int)Math.Round(slPips * 1.8m);  // min R:R 1.8
            stopLoss  = Math.Round(entry - slPips * 0.0001m, 5);
            takeProfit = Math.Round(entry + tpPips * 0.0001m, 5);
        }
        else if (signal == SignalDirection.SELL)
        {
            var rawSl = Math.Max(resistance - entry, 0.0010m);
            slPips    = (int)Math.Clamp(Math.Round(rawSl / 0.0001m), 10, 60);
            tpPips    = (int)Math.Round(slPips * 1.8m);
            stopLoss  = Math.Round(entry + slPips * 0.0001m, 5);
            takeProfit = Math.Round(entry - tpPips * 0.0001m, 5);
        }
        else // HOLD — parameter default
        {
            slPips    = 20; tpPips = 36;
            stopLoss  = Math.Round(entry - 0.0020m, 5);
            takeProfit = Math.Round(entry + 0.0036m, 5);
        }

        // Lot size = riskAmount / (slPips × $10/pip), minimum 0.01
        decimal lotSize        = Math.Max(Math.Round(riskAmount / (slPips * 10m), 2), 0.01m);
        decimal potentialProfit = Math.Round(lotSize * tpPips * 10m, 2);
        decimal riskReward     = slPips > 0 ? Math.Round((decimal)tpPips / slPips, 2) : 1.8m;

        return new TradeParameters(
            Entry:           entry,
            StopLoss:        stopLoss,
            StopLossPips:    slPips,
            TakeProfit:      takeProfit,
            TakeProfitPips:  tpPips,
            LotSize:         lotSize,
            RiskAmount:      riskAmount,
            PotentialProfit: potentialProfit,
            RiskRewardRatio: riskReward);
    }

    // ── Warnings ──────────────────────────────────────────────────────────────
    private static List<string> BuildWarnings(
        MarketSnapshot snap, SignalDirection signal,
        decimal confidenceScore, TrendAnalysis trend, MomentumAnalysis momentum)
    {
        var w = new List<string>();

        if (snap.RSI14 >= 70)
            w.Add($"RSI {snap.RSI14:F1} — overbought, risiko reversal untuk BUY.");
        if (snap.RSI14 <= 30)
            w.Add($"RSI {snap.RSI14:F1} — oversold, risiko reversal untuk SELL.");
        if (!trend.HtfAligned)
            w.Add("HTF (H1) berlawanan arah dengan M15 — trade counter-trend berisiko.");
        if (confidenceScore < 0.65m)
            w.Add($"Confidence {confidenceScore:P0} di bawah 70% — trade dengan ukuran lebih kecil.");
        if (signal == SignalDirection.HOLD)
            w.Add("Indikator tidak konsensus — tunggu konfirmasi lebih lanjut.");

        return w;
    }
}
