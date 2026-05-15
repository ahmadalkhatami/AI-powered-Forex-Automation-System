using System.Globalization;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Services;

/// <summary>
/// Menganalisis sinyal trading secara real-time dari data MIFX EA (MT5).
/// Menggantikan BmadSignalAnalyzer yang membaca dari file JSON statis.
/// Semua score dihitung ulang setiap kali Trigger Analysis dipanggil.
/// </summary>
public class LiveSignalAnalyzer : ISignalAnalyzer
{
    private readonly IBrokerService _broker;
    private readonly ISystemStateService _systemState;

    public LiveSignalAnalyzer(IBrokerService broker, ISystemStateService systemState)
    {
        _broker      = broker;
        _systemState = systemState;
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

        // ── 4b. Regime filter — override sinyal jika market sideway ────────
        signal = ApplyRegimeFilter(signal, snap, structure);

        // ── 4c. Setup vetos — accuracy filters berdasarkan analisa post-mortem 2 LOSS trade ─
        var (vetoSignal, vetoReasons) = ApplySetupVetos(signal, snap, momentum, pctFromSupport);
        signal = vetoSignal;

        // ── 4d. Cooldown veto — block same direction setelah LOSS ──────────
        if (signal != SignalDirection.HOLD &&
            _systemState.IsInCooldown(signal, out var minRemaining))
        {
            vetoReasons.Add($"VETO: {signal} di-override ke HOLD — cooldown post-LOSS aktif {minRemaining} menit lagi.");
            signal = SignalDirection.HOLD;
        }

        // ── 5. Scores ───────────────────────────────────────────────────────
        var (confluenceScore, confidenceScore) = CalculateScores(trend, momentum, structure, signal, snap.Regime);

        // ── 6. Trade parameters (1% risk rule) ─────────────────────────────
        var parameters = CalculateParameters(snap, signal, equity);

        // ── 7. Warnings ─────────────────────────────────────────────────────
        var warnings = BuildWarnings(snap, signal, confidenceScore, trend, momentum);
        foreach (var v in vetoReasons) warnings.Add(v);

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
    // Voting kondisi bullish: priceAboveMA20_M15, MA20>MA50_M15, MA20>MA50_H1, (opt) MA20>MA50_D1
    // D1 vote di-skip jika MA20_D1=0 (candle D1 belum cukup) → fallback ke 3-vote system.
    private static (TrendAnalysis result, decimal bullishBias) AnalyzeTrend(MarketSnapshot snap)
    {
        bool priceAboveMA20  = snap.CurrentPrice > snap.MA20_M15;
        bool ma20AboveMA50   = snap.MA20_M15     > snap.MA50_M15;
        bool h1MA20AboveMA50 = snap.MA20_H1      > snap.MA50_H1;
        bool hasD1           = snap.MA20_D1 > 0m && snap.MA50_D1 > 0m;
        bool d1MA20AboveMA50 = hasD1 && snap.MA20_D1 > snap.MA50_D1;

        // Setiap kondisi bullish = +1 poin dari total votes
        int totalVotes = hasD1 ? 4 : 3;
        decimal bullishPoints = (priceAboveMA20 ? 1m : 0m)
                              + (ma20AboveMA50   ? 1m : 0m)
                              + (h1MA20AboveMA50 ? 1m : 0m)
                              + (hasD1 && d1MA20AboveMA50 ? 1m : 0m);
        decimal bullishBias = bullishPoints / totalVotes; // 0=bear, 1=bull

        // Score = seberapa KONSISTEN semua indikator (0 = campur aduk, 1 = semua setuju)
        decimal trendScore = Math.Abs(bullishBias - 0.5m) * 2m;

        string bias     = bullishBias >= 0.6m ? "Bullish" : bullishBias <= 0.4m ? "Bearish" : "Neutral";
        string strength = trendScore switch { >= 0.8m => "Kuat", >= 0.5m => "Sedang", _ => "Lemah" };
        // HTF alignment: M15 bias harus sejajar dengan tertinggi yang tersedia (D1 > H1)
        bool htfTrend   = hasD1 ? d1MA20AboveMA50 : h1MA20AboveMA50;
        bool htfAligned = (bullishBias >= 0.5m) == htfTrend;

        string config = hasD1
            ? $"M15: MA20={snap.MA20_M15:F5} MA50={snap.MA50_M15:F5} | H1: MA20={snap.MA20_H1:F5} MA50={snap.MA50_H1:F5} | D1: MA20={snap.MA20_D1:F5} MA50={snap.MA50_D1:F5}"
            : $"M15: MA20={snap.MA20_M15:F5} MA50={snap.MA50_M15:F5} | H1: MA20={snap.MA20_H1:F5} MA50={snap.MA50_H1:F5}";

        string d1Suffix = hasD1
            ? $"; D1 {(d1MA20AboveMA50 ? "bullish" : "bearish")}"
            : "";

        string rationale =
            $"Price {(priceAboveMA20 ? "di atas" : "di bawah")} MA20 M15; " +
            $"MA20 {(ma20AboveMA50 ? ">" : "<")} MA50 M15 ({(ma20AboveMA50 ? "bullish" : "bearish")}); " +
            $"H1 {(h1MA20AboveMA50 ? "bullish" : "bearish")}{d1Suffix} — HTF {(htfAligned ? "sejajar" : "berlawanan")}";

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

        // Score: semakin dekat ke level kunci (S/R) → semakin tinggi kualitas setup
        // ≤10%: sangat dekat level → setup ideal (0.85)
        // ≤20%: dekat level        → setup bagus  (0.80)
        // ≤35%: agak dekat         → lumayan       (0.65)
        // >35%: mid-range          → setup lemah   (0.40)
        decimal score;
        string position;

        if      (pctFromSupport <= 0.10m || pctFromSupport >= 0.90m)
                                          { score = 0.85m; position = "Sangat dekat level"; }
        else if (pctFromSupport <= 0.20m || pctFromSupport >= 0.80m)
                                          { score = 0.80m; position = "Near support/resistance"; }
        else if (pctFromSupport <= 0.35m || pctFromSupport >= 0.65m)
                                          { score = 0.65m; position = "Mid-range";       }
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

    // ── Regime Filter ─────────────────────────────────────────────────────────
    // Ranging (ADX < 20): force HOLD kecuali price sangat dekat S/R (structure ≥ 0.80)
    // Transitional: biarkan sinyal jalan tapi akan ada warning + penalti confidence
    // Trending: boost sinyal, tidak ada override
    // Volatile: tidak ada override tapi warning wajib
    private static SignalDirection ApplyRegimeFilter(
        SignalDirection signal, MarketSnapshot snap, StructureAnalysis structure)
    {
        if (snap.Regime != "Ranging") return signal;     // non-ranging: tidak di-override

        // Ranging + price sangat dekat S/R → boleh trade mean-reversion
        if (structure.Score >= 0.80m) return signal;

        // Ranging + mid-range → tidak ada setup yang layak
        return SignalDirection.HOLD;
    }

    // ── Setup Vetos — block trade pada kondisi reversal/overextension ────────
    // Lessons learned dari live trading (33% WR, 68% SELL counter-trend momentum):
    // 1. Momentum-direction (counter-trend): block bila RSI direction lawan signal
    // 2. Mid-range: tidak ada edge S/R → reject
    // 3. Structure mismatch: SELL near support / BUY near resistance
    // 4. RSI extreme: SELL pada RSI ≤ 45 / BUY pada RSI ≥ 55 (tightened dari 35/65)
    // 5. Overextension: distance dari MA20 > 2 × ATR
    private static (SignalDirection result, List<string> reasons) ApplySetupVetos(
        SignalDirection signal, MarketSnapshot snap, MomentumAnalysis momentum, decimal pctFromSupport)
    {
        var reasons = new List<string>();
        if (signal == SignalDirection.HOLD) return (signal, reasons);

        // VETO 1 — Counter-trend momentum (RSI direction melawan signal)
        // Live data: 68% SELL signal terjadi saat RSI rising → momentum melawan = catching falling knife.
        bool rsiRising  = momentum.RSIDirection.Equals("rising",  StringComparison.OrdinalIgnoreCase);
        bool rsiFalling = momentum.RSIDirection.Equals("falling", StringComparison.OrdinalIgnoreCase);
        if (signal == SignalDirection.SELL && rsiRising)
        {
            reasons.Add($"VETO: SELL di-override ke HOLD — RSI direction RISING, momentum melawan signal (counter-trend).");
            return (SignalDirection.HOLD, reasons);
        }
        if (signal == SignalDirection.BUY && rsiFalling)
        {
            reasons.Add($"VETO: BUY di-override ke HOLD — RSI direction FALLING, momentum melawan signal (counter-trend).");
            return (SignalDirection.HOLD, reasons);
        }

        // VETO 2 — Mid-range sejati (true 50/50 zone): tidak ada edge S/R sama sekali.
        // Tightened ke 0.45-0.55 (dari 0.35-0.65) — voting system sudah handle setup lemah
        // dengan structure score 0.40; veto ini cuma block kasus benar-benar mid (zero edge).
        if (pctFromSupport > 0.45m && pctFromSupport < 0.55m)
        {
            reasons.Add($"VETO: {signal} di-override ke HOLD — price {pctFromSupport:P0} dari range = true mid-range, tidak ada edge S/R.");
            return (SignalDirection.HOLD, reasons);
        }

        // VETO 3 — Structure mismatch: SELL near support / BUY near resistance
        // Price < 25% dari S→R range = sangat dekat support (bounce risk untuk SELL)
        // Price > 75% dari S→R range = sangat dekat resistance (rejection risk untuk BUY)
        if (signal == SignalDirection.SELL && pctFromSupport <= 0.25m)
        {
            reasons.Add($"VETO: SELL di-override ke HOLD — price {pctFromSupport:P0} dari range terlalu dekat SUPPORT, risiko bounce.");
            return (SignalDirection.HOLD, reasons);
        }
        if (signal == SignalDirection.BUY && pctFromSupport >= 0.75m)
        {
            reasons.Add($"VETO: BUY di-override ke HOLD — price {pctFromSupport:P0} dari range terlalu dekat RESISTANCE, risiko rejection.");
            return (SignalDirection.HOLD, reasons);
        }

        // VETO 4 — RSI extreme zone (true overbought/oversold: 30/70 standar industri)
        // Sebelumnya threshold 45/55 terlalu ketat — RSI 55-65 itu zona bullish biasa, bukan extreme.
        // V1 (counter-trend RSI direction) sudah catch kasus "menjual saat RSI naik dari 36" —
        // V4 ini cuma safety net untuk extreme sejati (≥70 atau ≤30).
        if (signal == SignalDirection.SELL && momentum.RSIValue <= 30m)
        {
            reasons.Add($"VETO: SELL di-override ke HOLD — RSI {momentum.RSIValue:F1} ≤ 30 (oversold sejati), risiko reversal.");
            return (SignalDirection.HOLD, reasons);
        }
        if (signal == SignalDirection.BUY && momentum.RSIValue >= 70m)
        {
            reasons.Add($"VETO: BUY di-override ke HOLD — RSI {momentum.RSIValue:F1} ≥ 70 (overbought sejati), risiko reversal.");
            return (SignalDirection.HOLD, reasons);
        }

        // VETO 5 — Overextension dari MA20 M15 (>2 × ATR)
        // Mencegah entry di tail-end momentum, mean-reversion lebih mungkin terjadi.
        if (snap.ATR14 > 0m)
        {
            decimal distFromMa20 = Math.Abs(snap.CurrentPrice - snap.MA20_M15);
            decimal threshold    = 2m * snap.ATR14;
            if (distFromMa20 > threshold)
            {
                decimal distPips     = Math.Round(distFromMa20 / 0.0001m, 1);
                decimal thresholdPips = Math.Round(threshold / 0.0001m, 1);
                reasons.Add($"VETO: {signal} di-override ke HOLD — price {distPips}p dari MA20 (>{thresholdPips}p = 2×ATR), overextended.");
                return (SignalDirection.HOLD, reasons);
            }
        }

        return (signal, reasons);
    }

    // ── Confluence & Confidence Scores ────────────────────────────────────────
    private static (int confluenceScore, decimal confidenceScore) CalculateScores(
        TrendAnalysis trend, MomentumAnalysis momentum, StructureAnalysis structure,
        SignalDirection signal, string regime = "Unknown")
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

        // ── Regime confidence adjustment ────────────────────────────────────
        // Trending:      indikator lebih reliable → bonus +5%
        // Ranging:       MA kurang reliable, forced HOLD → penalti -10%
        // Transitional:  ketidakpastian → penalti kecil -3%
        // Volatile/Unknown: tidak ada penyesuaian
        confidenceScore = regime switch
        {
            "Trending"     => Math.Min(confidenceScore + 0.05m, 0.95m),
            "Ranging"      => Math.Max(confidenceScore - 0.10m, 0.35m),
            "Transitional" => Math.Max(confidenceScore - 0.03m, 0.35m),
            _              => confidenceScore
        };

        return (confluenceScore, Math.Round(confidenceScore, 2));
    }

    // ── Trade Parameters — ATR-based Dynamic SL/TP + Tier-aware Risk % ───────
    // SL = kSL × ATR(14)M15   →  market volatile: SL otomatis lebih lebar
    // TP = kTP × ATR(14)M15   →  R:R tetap konstan = kTP/kSL = 2.5/1.5 ≈ 1.67
    //
    // Risk per trade = tier.RiskPerTradePct × equity:
    //   starter (<$100):  2.0%   ← modal kecil butuh % besar agar 0.01 lot viable
    //   growth  (<$200):  1.5%
    //   stable  (<$500):  1.0%   ← standar industri
    //   scaled  (>$500):  1.0%
    //
    // Contoh ATR 12 pip:  SL = 1.5 × 12 = 18 pip,  TP = 2.5 × 12 = 30 pip
    // Contoh ATR 20 pip:  SL = 1.5 × 20 = 30 pip,  TP = 2.5 × 20 = 50 pip
    private static TradeParameters CalculateParameters(
        MarketSnapshot snap, SignalDirection signal, decimal equity)
    {
        var tier           = RiskTier.FromEquity(equity);
        decimal riskAmount = Math.Round(equity * tier.RiskPerTradePct, 2);
        decimal entry      = snap.CurrentPrice;
        const decimal pipSize = 0.0001m;  // EURUSD: 1 pip = 0.0001

        // ── ATR-based SL/TP ──────────────────────────────────────────────────
        const decimal kSL = 1.5m;   // SL multiplier
        const decimal kTP = 2.5m;   // TP multiplier  →  R:R = kTP/kSL = 1.67

        // Fallback 15 pip jika ATR belum ada (simulasi / EA v1.15 lama)
        decimal atrPips = snap.ATR14 > 0
            ? Math.Round(snap.ATR14 / pipSize, 1)
            : 15m;

        // Minimum SL = 15 pips → broker MIFX/MT5 retail biasanya butuh stop level ≥ 10-15 pips
        // (TRADE_RETCODE_INVALID_STOPS=10016 muncul jika SL terlalu dekat dengan harga).
        // Minimum TP = SL × 1.5 agar R:R minimal 1.5 dan TP juga lolos broker stop level.
        int slPips = (int)Math.Clamp(Math.Round(kSL * atrPips), 15m, 80m);
        int tpPips = (int)Math.Clamp(Math.Round(kTP * atrPips), Math.Max(slPips + 5m, slPips * 1.5m), 120m);

        decimal stopLoss, takeProfit;
        if (signal == SignalDirection.BUY)
        {
            stopLoss   = Math.Round(entry - slPips * pipSize, 5);
            takeProfit = Math.Round(entry + tpPips * pipSize, 5);
        }
        else if (signal == SignalDirection.SELL)
        {
            stopLoss   = Math.Round(entry + slPips * pipSize, 5);
            takeProfit = Math.Round(entry - tpPips * pipSize, 5);
        }
        else  // HOLD — tampilkan parameter simetris
        {
            stopLoss   = Math.Round(entry - slPips * pipSize, 5);
            takeProfit = Math.Round(entry + tpPips * pipSize, 5);
        }

        // Lot size = riskAmount / (slPips × $10/pip), minimum 0.01
        decimal lotSize         = Math.Max(Math.Round(riskAmount / (slPips * 10m), 2), 0.01m);
        decimal potentialProfit = Math.Round(lotSize * tpPips * 10m, 2);
        decimal riskReward      = slPips > 0 ? Math.Round((decimal)tpPips / slPips, 2) : (kTP / kSL);

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

        // ── Regime-specific warnings ─────────────────────────────────────────
        if (snap.ADX14 > 0)
        {
            switch (snap.Regime)
            {
                case "Ranging":
                    w.Add($"ADX {snap.ADX14:F1} — market ranging (sideway). Sinyal MA kurang reliable; " +
                          (signal == SignalDirection.HOLD ? "sinyal di-override ke HOLD." : "entry hanya di dekat S/R."));
                    break;
                case "Transitional":
                    w.Add($"ADX {snap.ADX14:F1} — market transitional. Mungkin mulai trending atau masih sideway — tunggu konfirmasi.");
                    break;
                case "Volatile":
                    w.Add($"ADX {snap.ADX14:F1} — market volatile / strongly trending. Spread mungkin melebar; pakai SL lebih lebar.");
                    break;
                case "Trending":
                    // Tidak ada warning — ini kondisi ideal untuk trend following
                    break;
            }
        }

        return w;
    }
}
