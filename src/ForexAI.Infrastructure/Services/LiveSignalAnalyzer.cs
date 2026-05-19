using System.Globalization;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure.Mifx;

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
    private readonly IModeService _mode;
    private readonly MifxCandleFeed _candleFeed;
    private readonly IAdaptiveStateService? _adaptiveState;  // optional — null safe untuk BacktestRunner stub

    public LiveSignalAnalyzer(
        IBrokerService broker,
        ISystemStateService systemState,
        IModeService mode,
        MifxCandleFeed candleFeed,
        IAdaptiveStateService? adaptiveState = null)
    {
        _broker        = broker;
        _systemState   = systemState;
        _mode          = mode;
        _candleFeed    = candleFeed;
        _adaptiveState = adaptiveState;
    }

    /// <summary>Breakout detection hasil — dipakai untuk override HOLD + boost confidence.</summary>
    private enum BreakoutDirection { None, Bullish, Bearish }
    private record BreakoutInfo(
        BreakoutDirection Direction,
        decimal LevelPrice,
        decimal PipsBeyond,
        int ConfirmationsPassed,  // 0-3 dari: strong body, volume, RSI align
        bool Compression,         // pre-breakout volatility squeeze
        bool LikelyFakeout,       // wick rejection di candle breakout — entry risky
        string Rationale);

    /// <summary>
    /// Drop the forming (in-flight) bar from EA payload supaya detector/analyzer pakai
    /// last CLOSED bar. EA kirim CopyRates(start=0, count) → index N-1 = current forming bar.
    /// Tanpa drop, pattern/breakout/sweep bisa false-detect karena bar masih bergerak.
    /// </summary>
    private static IReadOnlyList<CandleBar> ClosedBars(IReadOnlyList<CandleBar> bars)
        => bars.Count <= 1 ? bars : bars.Take(bars.Count - 1).ToList();

    /// <summary>
    /// Cooldown check with Adaptive Action 3 override support. Kalau Adaptive Engine
    /// sudah tune cooldown untuk direction tertentu, pakai itu; otherwise fallback
    /// ke baseline _systemState.CooldownMinutes.
    /// </summary>
    private bool IsInCooldownAdaptive(SignalDirection direction, out int minutesRemaining)
    {
        minutesRemaining = 0;
        if (_systemState.LastLossDirection != direction) return false;
        if (!_systemState.LastLossAt.HasValue) return false;

        int cooldownMin = _systemState.CooldownMinutes;
        if (_adaptiveState != null && !_adaptiveState.Current.MasterDisabled
            && _adaptiveState.Current.CooldownOverride.TryGetValue(direction.ToString(), out var adapt))
        {
            cooldownMin = adapt;
        }
        if (cooldownMin <= 0) return false;

        var elapsed = (DateTimeOffset.UtcNow - _systemState.LastLossAt.Value).TotalMinutes;
        if (elapsed >= cooldownMin) return false;
        minutesRemaining = (int)Math.Ceiling(cooldownMin - elapsed);
        return true;
    }

    /// <summary>
    /// Deteksi breakout multi-confirmation: close candle terakhir di luar high/low N bar
    /// sebelumnya + check fakeout filter + compression + 3-factor confirmation.
    /// Default lookback=20 (4 jam M15, 20 jam H1, 20 hari D1).
    /// </summary>
    private BreakoutInfo DetectBreakout(string pair, string timeframe, decimal rsi14, int lookback = 20)
    {
        var none = new BreakoutInfo(BreakoutDirection.None, 0m, 0m, 0, false, false, "");
        // +2: butuh 1 extra untuk drop forming bar, plus lookback+1 untuk akses current+prev N.
        var rawBars = _candleFeed.Get(pair, timeframe, lookback + 2);
        var bars = ClosedBars(rawBars);
        if (bars.Count < lookback + 1) return none;

        var current = bars[^1];
        var prevBars = bars.Take(bars.Count - 1).TakeLast(lookback).ToList();
        decimal highest = prevBars.Max(b => b.High);
        decimal lowest  = prevBars.Min(b => b.Low);

        BreakoutDirection dir = BreakoutDirection.None;
        decimal level = 0m, pipsBeyond = 0m;
        const decimal pipSize = 0.0001m;

        if (current.Close > highest)
        {
            dir = BreakoutDirection.Bullish;
            level = highest;
            pipsBeyond = Math.Round((current.Close - highest) / pipSize, 1);
        }
        else if (current.Close < lowest)
        {
            dir = BreakoutDirection.Bearish;
            level = lowest;
            pipsBeyond = Math.Round((lowest - current.Close) / pipSize, 1);
        }

        if (dir == BreakoutDirection.None) return none;

        // ── Fakeout filter — close near edge of candle range = wick rejection ─
        // Bullish breakout: close harusnya di upper 60% range (kalau lower = bearish reversal wick)
        decimal candleRange = current.High - current.Low;
        bool likelyFakeout = false;
        if (candleRange > 0m)
        {
            decimal closePosInRange = (current.Close - current.Low) / candleRange;  // 0..1
            likelyFakeout = (dir == BreakoutDirection.Bullish && closePosInRange < 0.4m) ||
                            (dir == BreakoutDirection.Bearish && closePosInRange > 0.6m);
        }

        // ── Multi-confirmation count (0-3) ────────────────────────────────────
        int confirmations = 0;
        var reasons = new List<string>();

        // Confirm 1: Strong body — body / range > 0.5 (bukan doji)
        decimal bodySize = Math.Abs(current.Close - current.Open);
        decimal bodyRatio = candleRange > 0m ? bodySize / candleRange : 0m;
        if (bodyRatio > 0.5m) { confirmations++; reasons.Add($"body {bodyRatio:P0}"); }

        // Confirm 2: Volume surge — volume > 1.2× avg of last 20 bar (kalau data available)
        if (current.Volume.HasValue)
        {
            var prevVolsWithValue = prevBars.Where(b => b.Volume.HasValue).Select(b => b.Volume!.Value).ToList();
            if (prevVolsWithValue.Count >= 10)
            {
                double avgVol = prevVolsWithValue.Average();
                if ((double)current.Volume.Value > avgVol * 1.2)
                {
                    confirmations++;
                    reasons.Add($"vol {current.Volume.Value / Math.Max(1d, avgVol):F1}×");
                }
            }
        }

        // Confirm 3: RSI align — bullish breakout butuh RSI > 55, bearish butuh RSI < 45
        if ((dir == BreakoutDirection.Bullish && rsi14 > 55m) ||
            (dir == BreakoutDirection.Bearish && rsi14 < 45m))
        {
            confirmations++;
            reasons.Add($"RSI {rsi14:F1}");
        }

        // ── Compression detection — pre-breakout volatility squeeze ───────────
        // Rata-rata range 5 bar terakhir < 0.7 × rata-rata range 20 bar sebelumnya.
        decimal avgRange20 = prevBars.Average(b => b.High - b.Low);
        var last5Bars = prevBars.TakeLast(5).ToList();
        decimal avgRange5 = last5Bars.Count >= 5 ? last5Bars.Average(b => b.High - b.Low) : avgRange20;
        bool compression = avgRange20 > 0m && avgRange5 < 0.7m * avgRange20;
        if (compression) reasons.Add("compression");

        string rationale = $"{pipsBeyond}p beyond {(dir == BreakoutDirection.Bullish ? "20-bar high" : "20-bar low")} ({level:F5})" +
                           (reasons.Count > 0 ? $" — {string.Join(", ", reasons)}" : "") +
                           (likelyFakeout ? " — FAKEOUT SUSPECT (wick rejection)" : "");

        return new BreakoutInfo(dir, level, pipsBeyond, confirmations, compression, likelyFakeout, rationale);
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

        var tier = RiskTier.FromEquity(equity, _mode.CurrentMode);

        // ── 1. Trend ────────────────────────────────────────────────────────
        var (trend, bullishBias) = AnalyzeTrend(snap);

        // ── 2. Momentum ─────────────────────────────────────────────────────
        var momentum = AnalyzeMomentum(snap);

        // ── 3. Structure ────────────────────────────────────────────────────
        var (structure, pctFromSupport) = AnalyzeStructure(snap);

        // ── 3b. Candlestick pattern detection — pakai 3 LAST CLOSED bar, bukan forming.
        // EA kirim CopyRates(start=0) sehingga bar terakhir masih in-progress → pattern bisa flip.
        var patternCandlesRaw = _candleFeed.Get(snap.Pair, snap.Timeframe, 4);
        var patternCandles = ClosedBars(patternCandlesRaw);
        var pattern = CandlestickPatternDetector.Detect(patternCandles);
        if (pattern.Name != "None")
        {
            structure = structure with { CandlePattern = pattern.Name };
        }

        // ── 4. Signal direction ─────────────────────────────────────────────
        var signal = DetermineSignal(bullishBias, momentum, pctFromSupport);

        // ── 4b. Regime filter — override sinyal jika market sideway ────────
        signal = ApplyRegimeFilter(signal, snap, structure);

        // ── 4c. Setup vetos — accuracy filters berdasarkan analisa post-mortem 2 LOSS trade ─
        var (vetoSignal, vetoReasons) = ApplySetupVetos(signal, snap, momentum, pctFromSupport);
        signal = vetoSignal;

        // ── 4d. Cooldown veto — block same direction setelah LOSS.
        // Adaptive Action 3 bisa override cooldown duration per direction; helper di bawah
        // baca AdaptiveState.CooldownOverride[direction] kalau ada, fallback ke baseline.
        if (signal != SignalDirection.HOLD && IsInCooldownAdaptive(signal, out var minRemaining))
        {
            vetoReasons.Add($"VETO: {signal} di-override ke HOLD — cooldown post-LOSS aktif {minRemaining} menit lagi.");
            signal = SignalDirection.HOLD;
        }

        // ── 4d-bis. Adaptive Session Skip (Action 2) — block kalau Adaptive Engine sudah
        //          flag session ini sebagai losing. Re-enable otomatis setelah skipUntil expired.
        if (signal != SignalDirection.HOLD && _adaptiveState != null && !string.IsNullOrEmpty(snap.Session))
        {
            var adaptive = _adaptiveState.Current;
            if (!adaptive.MasterDisabled
                && adaptive.SessionSkipUntil.TryGetValue(snap.Session, out var skipUntil)
                && skipUntil > DateTimeOffset.UtcNow)
            {
                var hoursLeft = (int)(skipUntil - DateTimeOffset.UtcNow).TotalHours;
                vetoReasons.Add(
                    $"ADAPTIVE SESSION SKIP: {signal} di-override ke HOLD — session {snap.Session} di-skip oleh Adaptive Engine " +
                    $"sampai {skipUntil:yyyy-MM-dd HH:mm} UTC ({hoursLeft}h tersisa).");
                signal = SignalDirection.HOLD;
            }
        }

        // ── 4f. Liquidity sweep detection — promote HOLD ke BUY/SELL kalau ada sweep
        //       (smart money just grabbed retail stops, high-probability reversal).
        //       Higher priority than breakout (reversal signal stronger).
        //       Pakai last CLOSED bar (drop forming) — sweep di in-flight candle bisa false.
        var sweepCandlesRaw = _candleFeed.Get(snap.Pair, snap.Timeframe, 23);
        var sweepCandles = ClosedBars(sweepCandlesRaw);
        var liquiditySweep = LiquidityDetector.DetectSweep(sweepCandles);
        if (liquiditySweep.Detected && signal == SignalDirection.HOLD)
        {
            SignalDirection promoteTo = liquiditySweep.Direction switch
            {
                "Bullish" => SignalDirection.BUY,
                "Bearish" => SignalDirection.SELL,
                _         => SignalDirection.HOLD
            };
            if (promoteTo != SignalDirection.HOLD && !IsInCooldownAdaptive(promoteTo, out _))
            {
                // Re-apply hard vetos — promote tidak boleh bypass reversal/overextension check.
                var (recheck, recheckReasons) = ApplyHardVetos(promoteTo, snap, momentum, pctFromSupport);
                if (recheck == SignalDirection.HOLD)
                {
                    vetoReasons.Add($"LIQUIDITY SWEEP DENIED: {promoteTo} promote blocked by hard veto — {string.Join(" | ", recheckReasons)}");
                }
                else
                {
                    vetoReasons.Add($"LIQUIDITY SWEEP: HOLD → {promoteTo} — {liquiditySweep.Description}");
                    signal = promoteTo;
                }
            }
        }

        // ── 4g. Round number magnet warning ─────────────────────────────────
        if (LiquidityDetector.IsNearRoundNumber(snap.CurrentPrice, out var roundLevel, out var pipsToRound))
        {
            vetoReasons.Add($"LIQUIDITY WARN: price {pipsToRound:F1}p dari round number {roundLevel:F4} — retail stop magnet, watch for stop hunt.");
        }

        // ── 4h. Breakout detection — multi-confirmation + fakeout filter + compression
        //       Promote HOLD ke BUY/SELL kalau breakout valid + aligned dengan D1.
        var breakout = DetectBreakout(snap.Pair, snap.Timeframe, snap.RSI14);
        if (breakout.Direction != BreakoutDirection.None)
        {
            bool d1Available = snap.MA20_D1 > 0m && snap.MA50_D1 > 0m;
            bool d1Bullish   = d1Available && snap.MA20_D1 > snap.MA50_D1;
            bool d1Bearish   = d1Available && snap.MA20_D1 < snap.MA50_D1;

            // Quality threshold untuk promote: minimal 2 confirmation atau compression+1 confirmation,
            // dan TIDAK fakeout. Conservative — better miss than enter fakeout.
            bool qualityBreakout = !breakout.LikelyFakeout &&
                                   (breakout.ConfirmationsPassed >= 2 ||
                                    (breakout.Compression && breakout.ConfirmationsPassed >= 1));

            if (breakout.LikelyFakeout)
            {
                vetoReasons.Add($"BREAKOUT IGNORED: {breakout.Rationale}");
            }
            else if (signal == SignalDirection.HOLD && qualityBreakout)
            {
                SignalDirection? promoteTo = null;
                bool d1AlignTag = false;
                if (breakout.Direction == BreakoutDirection.Bullish && !d1Bearish)
                {
                    promoteTo = SignalDirection.BUY;
                    d1AlignTag = d1Bullish;
                }
                else if (breakout.Direction == BreakoutDirection.Bearish && !d1Bullish)
                {
                    promoteTo = SignalDirection.SELL;
                    d1AlignTag = d1Bearish;
                }

                if (promoteTo.HasValue && !IsInCooldownAdaptive(promoteTo.Value, out _))
                {
                    // Re-apply hard vetos — breakout promote tidak boleh bypass reversal check.
                    var (recheck, recheckReasons) = ApplyHardVetos(promoteTo.Value, snap, momentum, pctFromSupport);
                    if (recheck == SignalDirection.HOLD)
                    {
                        vetoReasons.Add($"BREAKOUT DENIED: {promoteTo} promote blocked by hard veto — {string.Join(" | ", recheckReasons)}");
                    }
                    else
                    {
                        string alignSuffix = d1AlignTag ? (promoteTo == SignalDirection.BUY ? ", D1 bullish align." : ", D1 bearish align.") : "";
                        vetoReasons.Add($"BREAKOUT {promoteTo}: HOLD di-promote — {breakout.Rationale}{alignSuffix}");
                        signal = promoteTo.Value;
                    }
                }
            }
        }

        // ── 5. Scores ───────────────────────────────────────────────────────
        var (confluenceScore, confidenceScore) = CalculateScores(trend, momentum, structure, signal, snap.Regime);

        // ── 5a-bis. Liquidity sweep confidence boost — aligned reversal high edge.
        if (liquiditySweep.Detected &&
            ((liquiditySweep.Direction == "Bullish" && signal == SignalDirection.BUY) ||
             (liquiditySweep.Direction == "Bearish" && signal == SignalDirection.SELL)))
        {
            // Boost +0.08 (small sweep 1-3p) atau +0.12 (strong sweep >3p)
            decimal boost = liquiditySweep.PipsBeyond >= 3m ? 0.12m : 0.08m;
            confidenceScore = Math.Min(confidenceScore + boost, 0.95m);
            confluenceScore = Math.Min(confluenceScore + (int)Math.Round(boost * 100m), 100);
        }

        // ── 5b. Breakout confidence boost — scale by quality (confirmations + compression)
        //       Aligned breakout + 3 confirmations + compression = best setup = +0.15
        //       Aligned breakout + 1 confirmation, no compression       = mediocre  = +0.05
        if (breakout.Direction != BreakoutDirection.None && !breakout.LikelyFakeout &&
            ((breakout.Direction == BreakoutDirection.Bullish && signal == SignalDirection.BUY) ||
             (breakout.Direction == BreakoutDirection.Bearish && signal == SignalDirection.SELL)))
        {
            // Base boost dari pips beyond level: 0.03 (1-3p) / 0.06 (>3p)
            decimal boost = breakout.PipsBeyond >= 3m ? 0.06m : 0.03m;
            // Bonus per confirmation (0-3 × 0.025 = 0..0.075)
            boost += breakout.ConfirmationsPassed * 0.025m;
            // Compression bonus +0.04 (pre-breakout squeeze = high probability setup)
            if (breakout.Compression) boost += 0.04m;

            confidenceScore = Math.Min(confidenceScore + boost, 0.95m);
            confluenceScore = Math.Min(confluenceScore + (int)Math.Round(boost * 100m), 100);
        }

        // ── 5b-bis. Candlestick pattern boost/penalty — scale by pattern reliability ─
        //       Pattern aligned dengan signal (e.g. Bullish Pin Bar + BUY signal):
        //          boost = reliability × 0.10 (max +0.085 untuk Morning Star)
        //       Pattern counter signal (e.g. Bearish Engulfing + BUY signal):
        //          penalty = reliability × 0.10 (likely trap, lower confidence)
        //       Neutral pattern (Doji, Inside Bar): no change — consolidation, no edge.
        // Adaptive Action 4: pattern boost di-disable kalau Adaptive Engine sudah flag
        // pattern ini sebagai underperform. Penalty (counter) tetap apply — kita masih
        // mau hukum signal yang setup-nya lawan pattern terkenal, walaupun boost-nya off.
        bool patternBoostDisabled =
            _adaptiveState != null && !_adaptiveState.Current.MasterDisabled
            && _adaptiveState.Current.PatternDisableUntil.TryGetValue(pattern.Name, out var disableUntil)
            && disableUntil > DateTimeOffset.UtcNow;

        if (signal != SignalDirection.HOLD && pattern.Reliability >= 0.50m)
        {
            bool patternAligned =
                (pattern.Bias == "Bullish" && signal == SignalDirection.BUY) ||
                (pattern.Bias == "Bearish" && signal == SignalDirection.SELL);
            bool patternCounter =
                (pattern.Bias == "Bullish" && signal == SignalDirection.SELL) ||
                (pattern.Bias == "Bearish" && signal == SignalDirection.BUY);

            if (patternAligned && !patternBoostDisabled)
            {
                decimal boost = pattern.Reliability * 0.10m;
                confidenceScore = Math.Min(confidenceScore + boost, 0.95m);
                confluenceScore = Math.Min(confluenceScore + (int)Math.Round(boost * 100m), 100);
                vetoReasons.Add($"PATTERN ALIGNED: {pattern.Name} ({pattern.Reliability:F2} reliability) supports {signal} — conf +{boost * 100m:F0}%.");
            }
            else if (patternAligned && patternBoostDisabled)
            {
                vetoReasons.Add($"PATTERN BOOST DISABLED: {pattern.Name} di-disable oleh Adaptive Engine (underperform) — no confidence boost.");
            }
            else if (patternCounter)
            {
                decimal penalty = pattern.Reliability * 0.10m;
                confidenceScore = Math.Max(confidenceScore - penalty, 0.35m);
                confluenceScore = Math.Max(confluenceScore - (int)Math.Round(penalty * 100m), 0);
                vetoReasons.Add($"PATTERN COUNTER: {pattern.Name} ({pattern.Reliability:F2} reliability) lawan {signal} — possible trap, conf -{penalty * 100m:F0}%.");
            }
        }

        // ── 5b-bis. Adaptive Session Penalty (Action 2) — subtract confidence kalau
        //           Adaptive Engine sudah flag session ini sebagai underperform.
        if (signal != SignalDirection.HOLD && _adaptiveState != null && !string.IsNullOrEmpty(snap.Session))
        {
            var adaptive = _adaptiveState.Current;
            if (!adaptive.MasterDisabled &&
                adaptive.SessionPenalty.TryGetValue(snap.Session, out var sessionPenalty) &&
                sessionPenalty > 0m)
            {
                confidenceScore = Math.Max(confidenceScore - sessionPenalty, 0.35m);
                confluenceScore = Math.Max(confluenceScore - (int)Math.Round(sessionPenalty * 100m), 0);
                vetoReasons.Add(
                    $"ADAPTIVE SESSION PENALTY: {snap.Session} session underperform — conf -{sessionPenalty * 100m:F0}%.");
            }
        }

        // ── 5c. Premium/Discount Zone bias (ICT/SMC) ─────────────────────────
        // Zone berdasarkan posisi price di range Support-Resistance:
        //   pctFromSupport > 0.65 = PREMIUM (price mahal, ideal SELL)
        //   pctFromSupport < 0.35 = DISCOUNT (price murah, ideal BUY)
        //   0.35-0.65 = EQUILIBRIUM (no man's land)
        //
        // Counter-zone (BUY di premium / SELL di discount) = setup melawan zona
        //   = lower edge, kena confidence penalty.
        // Aligned-zone (BUY di discount / SELL di premium) = setup ideal SMC
        //   = confidence bonus.
        if (signal != SignalDirection.HOLD)
        {
            string zone = pctFromSupport > 0.65m ? "Premium" :
                          pctFromSupport < 0.35m ? "Discount" : "Equilibrium";

            bool buyInDiscount  = signal == SignalDirection.BUY  && zone == "Discount";
            bool sellInPremium  = signal == SignalDirection.SELL && zone == "Premium";
            bool buyInPremium   = signal == SignalDirection.BUY  && zone == "Premium";
            bool sellInDiscount = signal == SignalDirection.SELL && zone == "Discount";

            if (buyInDiscount || sellInPremium)
            {
                confidenceScore = Math.Min(confidenceScore + 0.05m, 0.95m);
                confluenceScore = Math.Min(confluenceScore + 5, 100);
                vetoReasons.Add($"ZONE ALIGNED: {signal} di {zone} zone ({pctFromSupport:P0} range) — ideal SMC setup, conf +5%.");
            }
            else if (buyInPremium || sellInDiscount)
            {
                confidenceScore = Math.Max(confidenceScore - 0.10m, 0.35m);
                confluenceScore = Math.Max(confluenceScore - 10, 0);
                vetoReasons.Add($"ZONE COUNTER: {signal} di {zone} zone ({pctFromSupport:P0} range) — lawan SMC zona, conf -10%.");
            }
            else  // Equilibrium
            {
                vetoReasons.Add($"ZONE: {signal} di Equilibrium ({pctFromSupport:P0} range) — no man's land, neutral.");
            }

            // Nano tier strict veto: counter-zone setup hard skip (modal kecil tidak afford lower-edge trade)
            if ((buyInPremium || sellInDiscount) && tier.Name == "nano")
            {
                vetoReasons.Add($"VETO Nano: {signal} counter-zone {zone} — modal kecil hanya trade aligned-zone setup.");
                signal = SignalDirection.HOLD;
            }
        }

        // ── 4e. Nano-mode extra vetos — STRICT quality threshold untuk modal kecil ─
        // Modal real <$100 = 5% per trade unavoidable (broker min lot). Untuk kompensasi,
        // butuh setup yang JAUH lebih bagus: confluence ≥ 80, conf ≥ 0.75, HTF full align,
        // regime Trending only, ATR < 25p (SL tidak terlalu lebar).
        if (signal != SignalDirection.HOLD && tier.Name == "nano")
        {
            decimal atrPips = snap.ATR14 > 0 ? snap.ATR14 / 0.0001m : 0m;
            bool d1Available = snap.MA20_D1 > 0m && snap.MA50_D1 > 0m;
            bool htfFullAligned = trend.HtfAligned && (!d1Available || (bullishBias >= 0.5m) == (snap.MA20_D1 > snap.MA50_D1));

            if (confluenceScore < 80)
            {
                vetoReasons.Add($"VETO Nano: confluence {confluenceScore} < 80 — modal kecil butuh setup A+.");
                signal = SignalDirection.HOLD;
            }
            else if (confidenceScore < 0.75m)
            {
                vetoReasons.Add($"VETO Nano: confidence {confidenceScore:P0} < 75% — modal kecil butuh kepastian tinggi.");
                signal = SignalDirection.HOLD;
            }
            else if (!htfFullAligned)
            {
                vetoReasons.Add("VETO Nano: HTF (M15+H1+D1) tidak sejajar — butuh full alignment di nano mode.");
                signal = SignalDirection.HOLD;
            }
            else if (snap.Regime != "Trending")
            {
                vetoReasons.Add($"VETO Nano: regime {snap.Regime} — nano mode hanya trade saat Trending.");
                signal = SignalDirection.HOLD;
            }
            else if (atrPips > 25m)
            {
                vetoReasons.Add($"VETO Nano: ATR {atrPips:F1}p > 25 — SL terlalu lebar untuk modal kecil.");
                signal = SignalDirection.HOLD;
            }
        }

        // ── 6. Trade parameters (mode + tier-aware risk) ──────────────────
        var parameters = CalculateParameters(snap, signal, equity, _mode.CurrentMode);

        // ── 6b. Defensive SL adjustment — push SL beyond swing levels (anti stop hunt)
        if (signal != SignalDirection.HOLD && sweepCandles.Count >= 20)
        {
            var swings = LiquidityDetector.FindSwingPoints(sweepCandles, lookback: 20);
            var adjustment = LiquidityDetector.AdjustStopLoss(parameters.StopLoss, parameters.Entry, signal, swings);
            if (adjustment != null)
            {
                vetoReasons.Add(adjustment.Reason);
                // Recalculate lot supaya risk amount tetap (SL lebar → lot lebih kecil)
                int newSlPips = (int)Math.Round(Math.Abs(parameters.Entry - adjustment.AdjustedSl) / 0.0001m);
                decimal newLot = Math.Max(Math.Round(parameters.RiskAmount / (newSlPips * 10m), 2), 0.01m);
                decimal newProfit = Math.Round(newLot * parameters.TakeProfitPips * 10m, 2);
                decimal newRR = newSlPips > 0 ? Math.Round((decimal)parameters.TakeProfitPips / newSlPips, 2) : parameters.RiskRewardRatio;
                parameters = parameters with {
                    StopLoss = adjustment.AdjustedSl,
                    StopLossPips = newSlPips,
                    LotSize = newLot,
                    PotentialProfit = newProfit,
                    RiskRewardRatio = newRR };
            }
        }

        // ── 7. Warnings ─────────────────────────────────────────────────────
        var warnings = BuildWarnings(snap, signal, confidenceScore, trend, momentum);
        foreach (var v in vetoReasons) warnings.Add(v);

        // ── Build adaptive learning enrich context (P0 trade journal) ─────────
        // Captured terlepas dari signal direction — even HOLD signals memberi data analytics.
        string entryZone = pctFromSupport > 0.65m ? "Premium" :
                           pctFromSupport < 0.35m ? "Discount" : "Equilibrium";

        var entryContext = new TradeEntryContext(
            Session:            SessionDetector.Detect(DateTimeOffset.UtcNow),
            Regime:             snap.Regime,
            PatternName:        pattern.Name == "None" ? null : pattern.Name,
            PatternBias:        pattern.Name == "None" ? null : pattern.Bias,
            PatternReliability: pattern.Name == "None" ? null : pattern.Reliability,
            SweepDetected:      liquiditySweep.Detected,
            Zone:               entryZone,
            Confidence:         confidenceScore);

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
            warnings:        warnings.AsReadOnly(),
            entryContext:    entryContext);
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
    // 4. RSI extreme: SELL pada RSI ≤ 30 / BUY pada RSI ≥ 70 (oversold/overbought sejati)
    // 5. Overextension: distance dari MA20 > 2 × ATR
    private static (SignalDirection result, List<string> reasons) ApplySetupVetos(
        SignalDirection signal, MarketSnapshot snap, MomentumAnalysis momentum, decimal pctFromSupport)
    {
        var (hardResult, hardReasons) = ApplyHardVetos(signal, snap, momentum, pctFromSupport);
        var htfReasons = BuildHtfModifierWarnings(hardResult, snap);
        hardReasons.AddRange(htfReasons);
        return (hardResult, hardReasons);
    }

    /// <summary>
    /// Hard vetos (5 rules) — block sinyal pada reversal/overextension.
    /// Dipisah dari HTF modifier supaya bisa di-re-apply setelah sweep/breakout promote
    /// (HTF modifier hanya log warning, tidak re-trigger).
    /// </summary>
    private static (SignalDirection result, List<string> reasons) ApplyHardVetos(
        SignalDirection signal, MarketSnapshot snap, MomentumAnalysis momentum, decimal pctFromSupport)
    {
        var reasons = new List<string>();
        if (signal == SignalDirection.HOLD) return (signal, reasons);

        // VETO 1 — Counter-trend momentum (RSI direction melawan signal)
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
        if (pctFromSupport > 0.45m && pctFromSupport < 0.55m)
        {
            reasons.Add($"VETO: {signal} di-override ke HOLD — price {pctFromSupport:P0} dari range = true mid-range, tidak ada edge S/R.");
            return (SignalDirection.HOLD, reasons);
        }

        // VETO 3 — Structure mismatch: SELL near support / BUY near resistance
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

        // VETO 4 — RSI extreme zone (≥70 atau ≤30)
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

    /// <summary>
    /// HTF D1 modifier — bukan hard veto. Tag warning supaya auto-approve gate boleh
    /// enforce confidence yang lebih tinggi (counter-D1 setup butuh +5%).
    /// </summary>
    private static List<string> BuildHtfModifierWarnings(SignalDirection signal, MarketSnapshot snap)
    {
        var reasons = new List<string>();
        if (signal == SignalDirection.HOLD) return reasons;
        if (snap.MA20_D1 <= 0m || snap.MA50_D1 <= 0m) return reasons;

        bool d1Bullish = snap.MA20_D1 > snap.MA50_D1;
        if (signal == SignalDirection.SELL && d1Bullish)
            reasons.Add($"HTF MODIFIER: D1 BULLISH (MA20 {snap.MA20_D1:F5} > MA50 {snap.MA50_D1:F5}), SELL counter-D1 — auto-approve butuh threshold+5%.");
        else if (signal == SignalDirection.BUY && !d1Bullish)
            reasons.Add($"HTF MODIFIER: D1 BEARISH (MA20 {snap.MA20_D1:F5} < MA50 {snap.MA50_D1:F5}), BUY counter-D1 — auto-approve butuh threshold+5%.");

        return reasons;
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
        MarketSnapshot snap, SignalDirection signal, decimal equity, TradeMode mode)
    {
        var tier           = RiskTier.FromEquity(equity, mode);
        decimal riskAmount = Math.Round(equity * tier.RiskPerTradePct, 2);
        decimal entry      = snap.CurrentPrice;
        const decimal pipSize = 0.0001m;  // EURUSD: 1 pip = 0.0001

        // ── ATR-based SL/TP ──────────────────────────────────────────────────
        // Nano mode: kTP turun 2.5 → 2.0 (R:R 1.33) — hit rate naik untuk modal kecil.
        const decimal kSL = 1.5m;
        decimal kTP = tier.Name == "nano" ? 2.0m : 2.5m;

        // Fallback 15 pip jika ATR belum ada (simulasi / EA v1.15 lama)
        decimal atrPips = snap.ATR14 > 0
            ? Math.Round(snap.ATR14 / pipSize, 1)
            : 15m;

        // Minimum SL = 15 pips, Minimum TP = max(SL+5, SL × 1.5)
        int slPips = (int)Math.Clamp(Math.Round(kSL * atrPips), 15m, 80m);
        int tpPipsBase = (int)Math.Clamp(Math.Round(kTP * atrPips), Math.Max(slPips + 5m, slPips * 1.5m), 120m);

        // ── Smart TP cap: jangan menembus S/R level berikutnya ────────────────
        // Untuk SELL: TP tidak boleh turun lebih jauh dari support − buffer (3 pip Nano, 5 pip lainnya)
        // Untuk BUY:  TP tidak boleh naik lebih jauh dari resistance + buffer
        // Ini meningkatkan hit rate karena price statistik bouncing di S/R, bukan breakout.
        decimal buffer = tier.Name == "nano" ? 3m * pipSize : 5m * pipSize;
        int tpPips = tpPipsBase;
        if (decimal.TryParse(snap.SupportZone,    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var support) &&
            decimal.TryParse(snap.ResistanceZone, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var resistance))
        {
            if (signal == SignalDirection.SELL && support > 0m && support < entry)
            {
                // Cap TP supaya tidak menembus support; minimal 5 pips di atas support buffer
                decimal tpCapPrice = support + buffer;  // close just above support
                int tpCapPips = (int)Math.Round((entry - tpCapPrice) / pipSize);
                if (tpCapPips >= slPips + 5 && tpCapPips < tpPips) tpPips = tpCapPips;
            }
            else if (signal == SignalDirection.BUY && resistance > 0m && resistance > entry)
            {
                decimal tpCapPrice = resistance - buffer;
                int tpCapPips = (int)Math.Round((tpCapPrice - entry) / pipSize);
                if (tpCapPips >= slPips + 5 && tpCapPips < tpPips) tpPips = tpCapPips;
            }
        }

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
