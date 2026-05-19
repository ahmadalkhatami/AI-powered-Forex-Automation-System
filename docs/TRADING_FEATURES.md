# Trading Features Catalog

Daftar fitur trading di project ini — **sudah implemented** vs **planned/considered**.

Status legend:
- ✅ **Live**: implemented + tested di production code
- 🚧 **Partial**: ada infrastructure tapi belum full feature
- 📋 **Planned**: di-evaluate, belum implement
- ❌ **Skip**: di-evaluate, decided not implement (with reason)

---

## 1. Signal Generation

| Feature | Status | File / Notes |
|---|---|---|
| Multi-timeframe analysis (M15 + H1 + D1) | ✅ Live | [LiveSignalAnalyzer.cs](../src/ForexAI.Infrastructure/Services/LiveSignalAnalyzer.cs) — trend voting across TF |
| MA20/MA50 crossover detection | ✅ Live | M15 + H1 + D1, ditrack via `MarketSnapshot` |
| RSI(14) momentum scoring | ✅ Live | Bullish/Bearish/Neutral zone + RSI direction |
| ATR(14) volatility-based SL/TP | ✅ Live | `CalculateParameters` — SL=1.5×ATR, TP=2.5×ATR (Standard) atau 2.0×ATR (Nano) |
| ADX(14) trend strength | ✅ Live | Regime detection: Ranging (<20), Transitional (20-25), Trending (25-40), Volatile (>40) |
| Support/Resistance level | ✅ Live | Dari EA tick payload, di-score by proximity |
| Confluence score (weighted) | ✅ Live | Trend 40% + Momentum 35% + Structure 25% |
| Confidence score (agreement-based) | ✅ Live | 1.0 − stddev × 2.5, regime-adjusted |

### Filters / Vetos / Modifiers
| Feature | Status | Notes |
|---|---|---|
| D1 HTF **modifier** | ✅ Live | **Bukan hard veto lagi** — counter-D1 setup pass, tapi auto-approve require conf ≥75% (vs 70% default). Allows pullback scalping. |
| Cooldown post-LOSS | ✅ Live | 30 min default, same-direction blocked |
| RSI extreme veto | ✅ Live | Skip BUY kalau RSI ≥ 70, SELL ≤ 30 |
| Overextension veto | ✅ Live | Skip kalau price > 2×ATR dari MA20 |
| Regime filter | ✅ Live | Force HOLD saat Ranging |
| **Premium/Discount Zone (ICT/SMC)** | ✅ Live | Aligned-zone (BUY discount / SELL premium) → conf +5%. Counter-zone (BUY premium / SELL discount) → conf -10%. Equilibrium 35-65% range = neutral. Nano tier: counter-zone hard veto. |
| Nano mode strict vetos | ✅ Live | Confluence ≥ 80, conf ≥ 75%, HTF full align, ATR < 25p, **counter-zone veto** |

---

## 2. Pattern Detection

### Candlestick Patterns
| Pattern | Status | File |
|---|---|---|
| Pin Bar (Bullish/Bearish) | ✅ Live | [CandlestickPatternDetector.cs](../src/ForexAI.Infrastructure/Services/CandlestickPatternDetector.cs) |
| Engulfing (Bullish/Bearish) | ✅ Live | Same |
| Doji | ✅ Live | Same |
| Marubozu (Bullish/Bearish) | ✅ Live | Same |
| Inside Bar | ✅ Live | Same |
| Morning/Evening Star (3-candle) | ✅ Live | Same |

### Smart Money Concepts
| Feature | Status | Notes |
|---|---|---|
| Fair Value Gap (FVG) | ✅ Live | [FairValueGapDetector.cs](../src/ForexAI.Infrastructure/Services/FairValueGapDetector.cs) — 3-candle gap, filled/unfilled tracking |
| **Liquidity Sweep detection** | ✅ Live | [LiquidityDetector.cs](../src/ForexAI.Infrastructure/Services/LiquidityDetector.cs) — wick beyond 20-bar high/low + close back inside. Promote HOLD → BUY/SELL (opposite direction = reversal entry). Confidence boost +0.08..+0.12. |
| **Swing High/Low pivot detection** | ✅ Live | Pivot points (high > N bar di kedua sisi) untuk defensive SL placement |
| **Defensive SL placement** | ✅ Live | Push SL 3p beyond nearest swing level (within 5p proximity) — anti stop hunt. Lot auto-recalculate untuk maintain risk amount. |
| **Round number magnet warning** | ✅ Live | Detect entry within ±5p dari 50/100 pip round number (e.g., 1.1700, 1.1650) — retail stop magnet, flag dengan warning |
| Order Block (OB) | ✅ Live | [OrderBlockDetector.cs](../src/ForexAI.Infrastructure/Services/OrderBlockDetector.cs) — last opposite candle sebelum 3-bar impulse ≥ 10p. Endpoint `/api/pattern/orderblock`. [OrderBlockOverlay.tsx](../frontend/src/components/dashboard/OrderBlockOverlay.tsx) render zone dengan diagonal hatching + solid border (distinct dari FVG dashed). Unmitigated OB lebih bold, Mitigated faded. Label badge "OB ↑ 12p" / "OB ↓ 8p". Polling 60s. |
| Break of Structure (BOS) | ✅ Live | [StructureBreakDetector.cs](../src/ForexAI.Infrastructure/Services/StructureBreakDetector.cs) — close di atas latest swing high (Bullish) atau di bawah latest swing low (Bearish), CONTEXT: trend continuation. Triangle marker di chart. |
| Change of Character (CHoCH) | ✅ Live | Same detector — close di atas latest **Lower High** (reversal up) atau di bawah latest **Higher Low** (reversal down). Circle marker (Major significance) di chart, lebih bold daripada BOS. |

### Breakout Detection (terintegrasi dengan signal)
| Feature | Status | Notes |
|---|---|---|
| 20-bar high/low breakout | ✅ Live | Promote HOLD → BUY/SELL kalau aligned D1 |
| Multi-confirmation (3-factor) | ✅ Live | Body strength + Volume surge + RSI align |
| Fakeout filter (wick rejection) | ✅ Live | Skip kalau close di lower 40% range (bullish breakout) |
| Compression detection | ✅ Live | Pre-breakout volatility squeeze, boost +0.04 conf |
| Confidence boost per quality | ✅ Live | Scale +0.03..+0.18 berdasarkan confirmations + compression + pips beyond |
| Follow-through wait | ❌ Skip | Kontradiksi tujuan responsive (delay 15min M15) |

---

## 3. Risk Management

### Position Sizing
| Feature | Status | Notes |
|---|---|---|
| Tier-aware risk % | ✅ Live | nano (5%), starter (2%), growth (1.5%), stable+ (1%) — [RiskTier.cs](../src/ForexAI.Domain/ValueObjects/RiskTier.cs) |
| Confidence-weighted sizing | ✅ Live | `0.7 + (conf − 0.6) × 1.5`, clamped [0.7, 1.3] |
| Loss-adapt sizing | ✅ Live | `1 / (1 + consecutiveLosses × 0.25)` — 0=1.0, 1=0.8, 2=0.67 |
| Nano $ override slider | ✅ Live | Dashboard slider, respect user choice (skip dynamic) |

### Hard Limits
| Feature | Status | Notes |
|---|---|---|
| Max open positions (3) | ✅ Live | ExecuteTradeHandler check |
| Max drawdown (10%) | ✅ Live | Hard system stop |
| Weekly DD cap (5% default) | ✅ Live | [SystemStateService.cs](../src/ForexAI.Infrastructure/SystemStateService.cs) — auto-halt kalau rolling 7-day loss melebihi |
| Nano daily $ loss cap | ✅ Live | Default $5, halt sampai UTC midnight |
| Nano equity floor | ✅ Live | Default $20, permanent halt sampai manual review |
| Circuit breaker (3 cons LOSS) | ✅ Live | Auto-halt |
| Max trade/day | ✅ Live | Default 7/hari, count by `openedAt` UTC date. Configurable via Settings UI. |

### Trade Management (sudah open)
| Feature | Status | Notes |
|---|---|---|
| Time stop (auto-close by duration) | 🚧 Opt-in | **DISABLED by default** (per user choice 2026-05-19) — TP/SL sudah cover exit. Enable via Settings UI dengan set MaxHoldingMinutes > 0. Nano tier tetap hard 2h cap. |
| Trailing stop | ✅ Live | Trigger +1.5R peak, close kalau retrace ≥1R (Standard) |
| Breakeven trigger | ✅ Live | Setelah peak +1R, close kalau reverse ke entry |
| Smart TP cap di S/R | ✅ Live | TP tidak menembus level support/resistance (buffer 3-5 pip) |
| Partial exit (+1R / +2R) | 📋 Planned | Butuh broker partial close + state tracking |

### Spread Protection
| Feature | Status | Notes |
|---|---|---|
| Absolute spread cap | ✅ Live | Reject kalau current spread > MaxSpreadPips (2.5p default) |
| Spread spike detection | ✅ Live | Reject kalau current > 2.5× rolling avg 60 samples + > 1.5p — typically news event |

---

## 4. Analytics & Validation

| Feature | Status | Endpoint |
|---|---|---|
| Win rate / expectancy / RR | ✅ Live | `GET /api/analytics/performance` |
| By confidence band | ✅ Live | 60-70%, 70-80%, 80%+ |
| By regime | ✅ Live | Trending, Transitional, Ranging, Volatile |
| By session | ✅ Live | London, NY, Tokyo, Sydney |
| By timeframe | ✅ Live | M15, H1, D1 |
| **By pattern detected** | ✅ Live | Win rate per Pin Bar / Engulfing / Star / None |
| **By signal source** | ✅ Live | MA Cross vs Breakout Promoted — validate filter efficacy |
| MFE/MAE per trade | 📋 Planned | Max favorable/adverse excursion — butuh tick-level data |
| Drawdown chart timeline | 🚧 Partial | EquityCurve.tsx exists, minimal |
| Sharpe ratio / Profit factor | 📋 Planned | Calculation easy, display panel needed |
| Best trading hours heatmap | 📋 Planned | Group by hour-of-day |

---

## 5. Dashboard / UX

| Feature | Status | File |
|---|---|---|
| Real-time candlestick chart | ✅ Live | lightweight-charts v5, M15/H1/D1 switchable |
| Live PnL interpolation | ✅ Live | Bid/Ask based (not mid), 30s stale guard |
| Position box overlay (TradingView-style) | ✅ Live | [PositionBoxOverlay.tsx](../frontend/src/components/dashboard/PositionBoxOverlay.tsx) |
| Pattern highlight overlay | ✅ Live | [PatternHighlightOverlay.tsx](../frontend/src/components/dashboard/PatternHighlightOverlay.tsx) — dashed rect + label pill + connector |
| **S/R zone overlay (static)** | ✅ Live | [SupportResistanceOverlay.tsx](../frontend/src/components/dashboard/SupportResistanceOverlay.tsx) — semi-transparent zone band (±5p) + dashed edges. Plus prominent priceLine (solid 2px) dengan axis label badge "⬆ S" / "⬇ R". Auto-update dari `structure.nearestSupport/Resistance`. |
| **Dynamic S/R trendline overlay** | ✅ Live | [DynamicSROverlay.tsx](../frontend/src/components/dashboard/DynamicSROverlay.tsx) — auto-draw trendline yang connect 2-3 swing high terakhir (Dynamic Resistance, dashed red) dan swing low terakhir (Dynamic Support, dashed green). Markers di tiap pivot point. Strength label (Strong/Good/Weak) berdasarkan 3-point linear fit error. Endpoint: `GET /api/structure/dynamic?pair=X&timeframe=Y`. Polling 30s per TF. |
| Drawing tools (9 jenis) | ✅ Live | hline, trendline, ray, rectangle, text, measure, fib retrace, fib ext, snap |
| Drawing lock + color/thickness | ✅ Live | Per-pair+TF persisted ke localStorage |
| Session chip (NY/London/etc) | ✅ Live | [SessionChip.tsx](../frontend/src/components/dashboard/SessionChip.tsx) auto-update |
| R:R indicator di PositionCard | ✅ Live | Planned 1:RR + realized R bar + current price marker |
| Settings UI | ✅ Live | `/settings` route — edit thresholds tanpa edit file |
| Auto-trigger analysis | ✅ Live | New bar detection, auto-run pipeline |
| **Auto-approve dengan dynamic threshold** | ✅ Live | Configurable via Settings UI (default 70%). Counter-D1 setup auto-bumped +5%. Manual approve always available regardless. Exec button label menampilkan threshold aktif (e.g. "Exec ≥65%"). |
| Audit log viewer | ✅ Live | `/audit` route |
| Backtest UI | ✅ Live | `/backtest` route |
| FVG zone overlay on chart | ✅ Live | [FvgOverlay.tsx](../frontend/src/components/dashboard/FvgOverlay.tsx) — canvas overlay anchored ke formedAt → extend kanan. Unfilled FVG: semi-transparent fill + dashed edge + label badge "FVG 8p ↑/↓". Filled FVG: faded outline saja. Bullish emerald / Bearish red. Polling 60s. |
| Settings advanced (per-strategy) | 📋 Planned | Per-pair / per-TF config |
| Mobile responsive | 🚧 Partial | Belum di-test thorough |
| Sound notification | 📋 Planned | Signal fire / position close audio alert |
| Multi-TF MA alignment chip | 📋 Planned | Quick "M15↑ H1↑ D1↑" status indicator |

---

## 6. Execution

| Feature | Status | Notes |
|---|---|---|
| MIFX MT5 EA bridge | ✅ Live | mql5/ForexAI_Bridge.mq5 v1.22+ |
| Simulation mode | ✅ Live | Tanpa real broker |
| Demo mode (MIFX_DEMO) | ✅ Live | Real broker, demo account |
| Real mode (MIFX_REAL) | 🚧 Partial | Infrastructure ready, hard $ caps active untuk Nano |
| Auto-detect demo/real | ✅ Live | ModeService dari `AccountInfoString` |
| Order fill confirmation | ✅ Live | Open: awaited dengan `brokerResult.Success` check. Close: async dengan logged retry-allow (sebelumnya fire-and-forget tanpa log). |
| **Slippage tracking** | ✅ Live | Log requested entry vs `brokerResult.ExecutedPrice` per trade. Pip delta = adverse fill amount. Foundation untuk edge analysis. |
| Latency monitoring | 📋 Planned | Alert kalau tick > 500ms delay |

---

## 7. Recent Audit Response

External audit (Nov 2026) identified concerns about decision hierarchy + execution layer. Response implemented:

| Audit Concern | Status | Action |
|---|---|---|
| D1 HTF veto terlalu keras untuk M15 scalping | ✅ Fixed | Convert veto → modifier (require conf ≥75% counter-D1) |
| Execution layer fire-and-forget (close) | ✅ Fixed | `FireCloseWithLogging` wrap dengan async error handling + retry-allow on failure |
| Slippage tracking absent | ✅ Fixed | Log requested vs `ExecutedPrice` per trade |
| Max trade/day cap absent | ✅ Fixed | Default 7/hari, settable via Settings UI |
| "Filter heavy" → over-dampening | 🔄 Measure | Analytics endpoint `bySignalSource` track Breakout vs MA Cross win rate untuk validate. Wait for 30-50 trade sample. |
| Confluence stacking too heavy | ❌ Disagree | Stddev-based confidence by design — low agreement → low conf. Not a bug. |

---

## 8. Considered & Rejected

Feature yang di-evaluate tapi decide NOT implement:

| Feature | Reason |
|---|---|
| Breakout follow-through wait | Kontradiksi tujuan responsive (15min latency M15) |
| Liquidity Trap detection (full SMC) | Overlap signifikan dengan Fakeout Filter |
| News calendar integration | Butuh external API decision (ForexFactory/Investing/ECB) — flagged for future |
| Real-time tick-level backtest | Tick data heavy, current approach pakai candle-based sudah cukup |
| ML model untuk signal | Belum cukup labeled data (need 1000+ trades) — analytics dulu |
| Crypto pair support | Focus EUR/USD dulu sampai consistent profitability |
| Auto-arbitrage between brokers | Out of scope — single broker setup |

---

## 9. Adaptive Self-Learning System

📋 **Planned · roadmap discussion 2026-05-19** — sistem trading yang adaptif belajar dari hasil tanpa user prompt setiap loss. Full design + 4-phase rollout di [adaptive-learning-roadmap.md](adaptive-learning-roadmap.md).

### Tier Klasifikasi Parameter

| Tier | Risk | Action Type | Status |
|------|------|-------------|--------|
| **Tier 0** | Never auto-tune | Safety invariants (MaxDD, EquityFloor, MaxSpread, MaxWeeklyDD) | ✅ Hardcoded (locked) |
| **Tier 1** | Low risk, auto-fire | Session/regime penalty, cooldown length, pattern enable/disable | 📋 P2 target |
| **Tier 2** | Medium, manual approve | Pattern/breakout boost magnitude | 📋 P3 target |
| **Tier 3** | High risk, BLOCKED | SL multiplier, RR ratio, weight formula | ❌ Not planned |

### Phase Plan (high-level)

| Phase | Status | Description |
|-------|--------|-------------|
| **P0** Trade Journal Enrich | 📋 Pending | Tambah session, patternMatch, sweepDetected, zoneAtEntry, MFE, MAE, holdingMinutes, exitReason ke trade record |
| **P1** Observe Only | 📋 Pending | Dashboard panel per-bucket WR + expectancy + Wilson CI. **No auto-action.** |
| **P2** Tier 1 Activate | 📋 Pending | 4 Tier 1 actions auto-fire dengan audit + kill switches |
| **P3** Tier 2 Suggest-Only | 📋 Pending | Manual-approve UI untuk pattern boost tuning |
| **P4** Bayesian Bucket Scoring | 📋 Future | Thompson sampling — butuh 500+ trade |

### Tier 1 Actions (4 — P2 target)

1. **Per-regime confidence threshold** — bounds `[0.60, 0.85]`, sample ≥ 20, cooldown 24h
2. **Session penalty / skip** — penalty ≤ -15%, skip 7 hari auto re-enable
3. **Cooldown length adaptation** — bounds `[15, 120]` min, per-direction
4. **Pattern enable/disable** — binary on/off, 30 hari auto re-enable

### Statistical Safeguards

- **Global ≥ 50 total trade** sebelum Adaptive Engine aktif (warm-up gate)
- Min sample size 20 per bucket (30 untuk skip/disable)
- Wilson score interval lower bound test
- Rolling window 30 trade default
- Per-action cooldown 24h-30d
- Master + per-action kill switches di Settings UI
- Performance regression auto-revert kalau expectancy negatif ≥ 10 trade
- **Config snapshot before/after + reason JSON** sebelum tiap auto-change → 1-click rollback via Settings UI

### Progress Tracker

Update setiap milestone di [adaptive-learning-roadmap.md § 11](adaptive-learning-roadmap.md). Snapshot:

- P0: **8/8 task ✅ complete** (2026-05-19)
- P1: 0/5 task
- P2: 0/13 task
- P3: 0/4 task

---

## 10. Roadmap Indicative (Pre-Adaptive Wave)

Wave berikut ini sudah di-supersede sebagian oleh § 9 Adaptive Roadmap, tapi item non-adaptive (FE rendering, detector baru) tetap relevan:

**Wave 1** (data-driven validation):
1. Collect 30-50 trade dengan filter baru aktif
2. Check `/api/analytics/performance` — apakah breakout & pattern filter actually boost win rate
3. ~~Tune confidence/breakout thresholds based on real data~~ → digantikan Adaptive P2

**Wave 2** (kalau Wave 1 confirm edge):
1. FVG chart overlay (rendering di chart yang sudah ada endpoint)
2. CHoCH/BOS detector
3. News calendar integration
4. Order Block detection

**Wave 3** (production hardening):
1. Mobile-responsive review
2. Trade journal automation (screenshot + reason capture) → overlap dengan Adaptive P0
3. Multi-TF alignment indicator chip
4. Settings advanced (per-strategy)
