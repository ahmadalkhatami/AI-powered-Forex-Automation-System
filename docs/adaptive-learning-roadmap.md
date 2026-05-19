# Adaptive Self-Learning Trading System — Roadmap

**Status**: 📋 Planned · belum mulai coding
**Started discussion**: 2026-05-19
**Goal**: Sistem trading yang adaptif — belajar dari hasil trade tanpa user harus prompt "kenapa ini loss" lagi.

---

## 0. North Star

Bot menyesuaikan behavior **secara otomatis** berdasarkan rolling statistik, dengan **safety bounds** dan **transparent audit trail**. Setiap perubahan parameter harus bisa di-trace ke evidence statistik (sample size + p-value + delta vs baseline).

**Yang BUKAN tujuan**:
- ❌ Black-box ML prediction model (tidak interpretable, butuh data >1000 trade)
- ❌ Reinforcement learning di live money (sample inefficient, dangerous)
- ❌ Auto-adjustment terhadap SL/RR/risk-per-trade (tier 3 — terlalu mudah curve-fit)

---

## 1. Arsitektur 5-Layer

```
┌─────────────────────────────────────────────────────────────┐
│  Layer 1 — Signal Engine (✅ existing)                      │
│  MA + RSI + ATR + ADX + S/R + Pattern + FVG + Liquidity     │
│  Output: BUY / SELL / HOLD + confidence + warnings          │
└─────────────────────────────────────────────────────────────┘
                          ↓ produces feature vector + outcome
┌─────────────────────────────────────────────────────────────┐
│  Layer 2 — Trade Journal (🚧 partial, butuh enrich)         │
│  Setiap trade jadi "case study": entry features + market    │
│  conditions + execution metrics + outcome (WIN/LOSS, MFE,   │
│  MAE, slippage, holding time)                               │
└─────────────────────────────────────────────────────────────┘
                          ↓ rolling window analytics
┌─────────────────────────────────────────────────────────────┐
│  Layer 3 — Learning Engine (📋 to build)                    │
│  Statistical aggregation per bucket:                        │
│  - WR, expectancy, profit factor                            │
│  - Sample size + statistical test (Wilson interval / SPRT)  │
│  - Performance decay detection                              │
└─────────────────────────────────────────────────────────────┘
                          ↓ suggest action
┌─────────────────────────────────────────────────────────────┐
│  Layer 4 — Decision Modulator (📋 to build)                 │
│  Auto-apply Tier 1 actions (within safety bounds)           │
│  Display Tier 2 suggestions (manual approve)                │
│  Tier 3: BLOCKED for now                                    │
│  Tier 0: NEVER auto-tune (safety invariants)                │
└─────────────────────────────────────────────────────────────┘
                          ↓ audit + UI
┌─────────────────────────────────────────────────────────────┐
│  Layer 5 — Audit & UI (🚧 partial)                          │
│  Dashboard panel: per-bucket stats, current adjustments,    │
│  history of auto-changes, manual override controls          │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. Tier Klasifikasi Parameter

| Tier | Risk | Action Type | Examples |
|------|------|-------------|----------|
| **Tier 0** | Never auto-tune | Hardcoded safety | `MaxDrawdownPct`, `MaxConsecutiveLosses`, `NanoEquityFloorUsd`, `MaxSpreadPips`, `MaxWeeklyDrawdownPct` |
| **Tier 1** | Low risk, auto-fire | Reversible penalties/skips | Session penalty, regime confidence threshold, cooldown length, pattern enable/disable |
| **Tier 2** | Medium risk, manual approve | Magnitude tuning | Pattern boost amount, breakout boost amount, volatility weighting |
| **Tier 3** | High risk, BLOCKED | Continuous + global | SL multiplier, RR ratio, weight formula (trend/momentum/structure %) |

**Rule**: progress hanya Tier 0 → Tier 1. Tier 2 minimal 3-6 bulan trust calibration. Tier 3 mungkin tidak akan pernah.

---

## 3. Tier 1 Actions (Phase 2 target — 4 actions)

Setiap action: ada **trigger criteria**, **safety bounds**, **cooldown**, **audit log**, dan **kill switch** di Settings UI.

### 3.1 Per-Regime Confidence Threshold

**What**: threshold confidence untuk auto-execute beda per regime (Trending / Ranging / Transitional / Volatile).

**Baseline**: 0.70 (current single value)

**Logic**:
- Track WR per regime, rolling 30 trade
- Kalau regime X WR < 40%: naikkan threshold +0.05 (bot jadi lebih picky)
- Kalau regime X WR > 60%: turunkan threshold -0.05 (bot fire lebih sering)

**Safety bounds**: `[0.60, 0.85]` (max swing ±0.15 dari baseline)

**Trigger**: sample ≥ 20 trade per regime, p < 0.05 (Wilson interval)

**Cooldown**: 24h per regime — tidak boleh re-adjust dalam 24h setelah change

---

### 3.2 Session Penalty / Skip

**What**: penalti confidence per trading session (London / NY / Tokyo / Sydney / Overlap).

**Baseline**: 0% penalty (no discrimination)

**Logic**:
- Track WR per session, rolling 30 trade
- WR < 35% selama ≥ 20 trade → confidence penalty -5%
- WR < 25% selama ≥ 30 trade → skip session entirely (HOLD all signals)
- Re-enable session setelah 7 hari natural (tanpa intervention)

**Safety bounds**: penalty maksimum -15%, skip maksimum 7 hari sekali activation

**Trigger**: sample ≥ 20 trade per session

**Cooldown**: 7 hari setelah skip activate

---

### 3.3 Cooldown Length Adaptation

**What**: lama post-LOSS cooldown adaptive berdasarkan pattern losing streak.

**Baseline**: 30 menit (current)

**Logic**:
- Track: kalau setelah LOSS, next same-direction trade dalam 30 menit juga LOSS (≥ 70% kasus) → extend cooldown ke +15 menit
- Kalau next same-direction setelah cooldown WR ≥ 55% → shrink cooldown -10 menit

**Safety bounds**: `[15 min, 120 min]`

**Trigger**: sample ≥ 15 post-LOSS sequences

**Cooldown**: 48h per direction (BUY/SELL track terpisah)

---

### 3.4 Pattern Enable/Disable

**What**: binary on/off untuk specific pattern boost (Pin Bar / Engulfing / Star / Marubozu / Doji).

**Baseline**: semua pattern aktif (boost = reliability × 0.10)

**Logic**:
- Track WR per pattern detected, rolling 30 trade
- WR < 35% selama ≥ 20 trade → disable boost pattern itu (set boost = 0)
- Re-enable setelah 30 hari natural

**Safety bounds**: hanya boost yang di-disable, pattern detection tetap jalan untuk display. Tidak ada masking pattern bias arah.

**Trigger**: sample ≥ 20 trade per pattern

**Cooldown**: 30 hari setelah disable

---

## 4. Statistical Requirements

Untuk semua Tier 1 actions:

- **Global minimum trade count**: ≥ **50 total trade** sebelum adaptive engine sama sekali aktif. Walaupun bucket individu sudah 20 trade, sistem harus punya minimum overall sample untuk avoid early-stage false patterns.
- **Min sample size per bucket**: ≥ 20 (tighter 30 untuk skip/disable actions)
- **Confidence test**: Wilson score interval lower bound < baseline WR threshold
- **Rolling window**: 30 trade default (configurable)
- **Multi-comparison correction**: tidak diperlukan untuk Tier 1 small action space; di Tier 2/3 perlu Bonferroni atau FDR

**Rationale**:
1. WR per bucket bisa misleading dengan small sample. Mis. bucket 5 trade dengan 1 win = 20% WR tampak buruk, tapi 95% Wilson lower bound = ~1%, upper bound = ~70% — terlalu wide untuk action.
2. Global ≥ 50 trade floor mencegah Adaptive Engine "panic-tune" di 30 trade pertama yang biasanya volatile (warm-up period, system masih kalibrasi).

**Gate logic**:
```
adaptive_enabled = (totalTradeCount >= 50)
                    AND (bucket.tradeCount >= 20)
                    AND (wilson_lower_bound < baseline_threshold)
                    AND (cooldown_expired)
                    AND (master_kill_switch == false)
                    AND (action_kill_switch[action] == false)
```

---

## 5. Phase Plan

| Phase | Duration | Deliverable | Trigger |
|-------|----------|-------------|---------|
| **P0** Trade Journal Enrich | 1 minggu | Trade record include semua feature (regime, session, pattern, sweep, zone, slippage, MFE, MAE, holding time) | Code start setelah user OK roadmap |
| **P1** Observe Only | 2-4 minggu | Dashboard analytics panel per-bucket WR + expectancy + Wilson CI + sample size. **No auto-action**. Manual review only. | Setelah P0 complete |
| **P2** Tier 1 Activate | 2 minggu code + 4-6 minggu data | Implement 4 Tier 1 actions dengan audit log + kill switches. **Auto-fire enabled**. | Setelah P1 collect ≥ 30 trade post-fix |
| **P3** Tier 2 Suggest-Only | 1 bulan | Manual-approve UI: system suggest pattern boost magnitude / breakout boost tweak, user approve | Setelah P2 berjalan ≥ 80 trade, ≥ 2 Tier 1 actions fired correctly |
| **P4** Bayesian Bucket Scoring | TBD | Continuous Thompson sampling untuk parameter exploration | Setelah 500+ trade |

---

## 6. Trade Journal Enrichment (P0 Detail)

**Goal**: setiap trade record contain semua feature yang berguna untuk Learning Engine.

### Current state ([signal-history.json](../data/demo/signal-history.json))
- ✅ Confidence, confluence, signal direction, warnings
- ✅ Snapshot (MA, RSI, ATR, ADX, S/R)
- ✅ Parameters (entry, SL, TP, lot, RR)
- ✅ Pattern name di structure.candlePattern

### Missing fields untuk enrich

| Field | Source | Purpose |
|-------|--------|---------|
| `session` | Detect dari timestamp UTC hour | Bucket per session |
| `regime` | Sudah ada di snapshot | Pastikan ter-persist saat trade close |
| `patternMatch` | CandlestickPatternDetector output (name + bias + reliability) | Per-pattern WR |
| `sweepDetected` | LiquidityDetector output | Track sweep promotion accuracy |
| `zoneAtEntry` | Premium / Discount / Equilibrium | Per-zone WR |
| `slippagePips` | Sudah ada di execute log | Cost tracking |
| `mfePips` | Max favorable excursion during hold | Reward potential |
| `maePips` | Max adverse excursion during hold | Stop tightness |
| `holdingMinutes` | closedAt − openedAt | Time efficiency |
| `exitReason` | SL hit / TP hit / breakeven / trailing / manual | Cause analysis |

---

## 7. Learning Engine Service (P2 Implementation)

### Service: `AdaptiveLearningService` (new)

**Responsibility**:
- Compute rolling stats per bucket (regime, session, pattern, etc.)
- Check trigger criteria untuk setiap Tier 1 action
- Apply changes via `ISystemStateService.UpdateConfig` (sudah ada infrastructure)
- Log audit event with reason + evidence

**Trigger**: scheduled BackgroundService — run setiap N trade close (default: every 5 trade) atau every 6 jam.

### Storage: `AdaptiveStateService` (new)

**Persist**:
- Current per-regime confidence threshold (override base)
- Current session penalties + skip status + skip-expire-at
- Current cooldown length per direction
- Current per-pattern disable status + disable-expire-at
- Audit history (last 50 adjustments + reasons)

**File**: `data/{mode}/adaptive-state.json` (mode-aware seperti system-state.json)

### Endpoint: `GET /api/adaptive/state`

Returns current adaptive overrides + suggested-pending (Tier 2 nanti) + audit history.

---

## 8. Dashboard UI (P1 + P3)

### Adaptive Panel (`/adaptive` route, new)

**Sections**:
- **Current adjustments**: tabel parameter dengan baseline vs current vs trigger evidence
- **Per-bucket statistics**: tabel sortable WR / expectancy / sample size / Wilson CI per regime / session / pattern / confidence band
- **Audit history**: timeline of auto-adjustments dengan reason + impact
- **Manual override**: per-action toggle (disable Tier 1 action) + reset-to-baseline button
- **Tier 2 suggestions** (P3+): pending recommendations dengan Approve/Dismiss/Snooze buttons

### Settings UI (extend existing)

Tambah:
- `Adaptive Learning [Enable/Disable]` master toggle
- Per-Tier-1-action enable/disable toggles
- Rolling window size (default 30)

---

## 9. Safety Net & Kill Switches

| Layer | Safeguard |
|-------|-----------|
| **Tier 0** | Hardcoded — tidak exposed ke Adaptive Service. Code-level enforcement. |
| **Global trade floor** | Adaptive Engine **tidak aktif** sampai `totalTradeCount >= 50`. Lihat § 4. |
| **Bounds clamping** | Setiap adjustment di-clamp ke `[min, max]`. Tidak boleh swing wild. |
| **Cooldown per action** | 24h-30 hari sebelum same parameter bisa adjust lagi. |
| **Master kill switch** | Settings UI: disable seluruh Adaptive Learning kembali ke baseline. |
| **Per-action kill switch** | Settings UI: disable single Tier 1 action. |
| **Audit log** | Setiap change logged dengan reason + evidence + timestamp ke `audit-log.jsonl`. |
| **Config snapshot before/after** | **WAJIB** sebelum modify — lihat § 9.1 detail. Rollback + audit + debugging + edge analysis. |
| **Performance regression detector** | Kalau setelah auto-adjust expectancy NEGATIF selama ≥ 10 trade → auto-revert ke baseline + alert user. |
| **Manual override priority** | User adjustment di Settings UI override Adaptive untuk N hari. |

### 9.1 Config Snapshot Protocol (WAJIB)

Sebelum Adaptive Engine modify parameter apa pun, system tulis **3 file** ke `data/{mode}/adaptive-snapshots/{timestamp}-{action}/`:

```
data/{mode}/adaptive-snapshots/
└── 2026-05-19T143052Z-regime-threshold/
    ├── config_before.json      # full system-state.json + adaptive-state.json sebelum change
    ├── config_after.json       # full state setelah change
    └── reason.json             # evidence: bucket stats + wilson CI + sample size + delta + trigger rule
```

**`config_before.json`** — snapshot lengkap sebelum modify (untuk rollback instan):
```json
{
  "timestamp": "2026-05-19T14:30:52Z",
  "totalTradeCount": 87,
  "systemState": { ... full SystemStateService state ... },
  "adaptiveState": { ... full AdaptiveStateService state ... }
}
```

**`config_after.json`** — snapshot setelah modify (untuk diff visualization).

**`reason.json`** — evidence + decision rationale:
```json
{
  "action": "regime-confidence-threshold",
  "regime": "Ranging",
  "trigger": "WR below 40% threshold",
  "evidence": {
    "bucketTradeCount": 24,
    "winCount": 8,
    "lossCount": 16,
    "winRate": 0.333,
    "wilsonLower95": 0.171,
    "wilsonUpper95": 0.535,
    "baselineThreshold": 0.40,
    "expectancyR": -0.21
  },
  "change": {
    "parameter": "regimeConfidenceThreshold[Ranging]",
    "from": 0.70,
    "to": 0.75,
    "deltaClamped": "+0.05 (within bounds [0.60, 0.85])"
  },
  "cooldownUntil": "2026-05-20T14:30:52Z",
  "approvedBy": "AdaptiveLearningService.AutoFire"
}
```

**Retention**: keep last 50 snapshots per mode (rolling delete). Bisa di-tune via Settings.

**Use cases**:
1. **Rollback**: 1-click revert via Settings UI → pakai `config_before.json` overwrite current state
2. **Audit trail**: lihat history adjustment + alasan tanpa parse log raw
3. **Debugging**: kalau hasil trade post-adjust anomalous, bisa correlate dengan reason evidence
4. **Edge analysis**: aggregate `reason.json` dari banyak snapshot untuk lihat pattern (e.g., apakah Ranging regime sering trigger threshold raise → mungkin regime detector itu sendiri yang perlu di-tune)

**Endpoint**: `GET /api/adaptive/snapshots` return list + detail. `POST /api/adaptive/rollback/{snapshotId}` rollback ke before state (manual approval only).

---

## 10. Success Metrics

System dianggap learning yang baik kalau:

| Metric | Target | Measurement |
|--------|--------|-------------|
| **Expectancy improvement** | ≥ +0.05R per trade vs pre-adaptive baseline | Compare rolling 50 trade |
| **Drawdown maintained** | Max DD tidak naik | Same risk discipline |
| **WR per active bucket** | ≥ 45% across all active buckets | After 1 bulan adaptive |
| **Audit transparency** | Setiap action explainable dengan 1-line reason | Manual spot check |
| **No catastrophic auto-adjust** | Tidak ada rollback needed dalam 1 bulan | Audit log review |

---

## 11. Progress Tracker

Update setiap milestone selesai. Checkbox = done.

### P0 — Trade Journal Enrich
- [x] Add `session` field ke trade record (via `SessionDetector` + `TradePosition.SessionAtEntry`)
- [x] Add `patternMatch` (name+bias+reliability) ke trade record (`PatternName/PatternBias/PatternReliability`)
- [x] Add `sweepDetected` flag
- [x] Add `zoneAtEntry`
- [x] Add `mfePips` + `maePips` tracking (`TradePosition.TrackExcursion()` per tick sync)
- [x] Add `holdingMinutes` (computed property) + `exitReason` (SL_HIT/TP_HIT/BREAKEVEN/TRAILING_STOP/TIME_STOP/MANUAL)
- [x] Update `JsonTradePositionRepository` + `TradePositionDto` + `DtoMapper` schema (backward compat read — semua field nullable, trade lama auto-null)
- [x] Verify enrich via build + test (26/26 pass)

### P1 — Observe Only
- [ ] Backend: `AdaptiveAnalyticsService` compute rolling per-bucket stats
- [ ] Endpoint: `GET /api/adaptive/stats` per-bucket WR + expectancy + Wilson CI
- [ ] Frontend: `/adaptive` route — panel tabel + sort + filter
- [ ] Settings UI: master toggle (display only initially)
- [ ] 4 minggu observe — collect ≥ 30 trade

### P2 — Tier 1 Activate
- [ ] `AdaptiveStateService` — persist current overrides + audit history
- [ ] `AdaptiveLearningService` BackgroundService — scheduled trigger
- [ ] Global trade count gate (≥ 50) check di awal cycle
- [ ] **Config snapshot protocol** — before/after/reason JSON files per change
- [ ] Snapshot retention policy (last 50, rolling delete)
- [ ] Endpoint `GET /api/adaptive/snapshots` + `POST /api/adaptive/rollback/{id}`
- [ ] Action 1: Per-regime confidence threshold (with bounds + cooldown)
- [ ] Action 2: Session penalty/skip (with auto re-enable timer)
- [ ] Action 3: Cooldown length adaptation per direction
- [ ] Action 4: Pattern enable/disable (with auto re-enable timer)
- [ ] Performance regression detector — auto-revert
- [ ] Frontend: adjustment history timeline + per-action kill switch + rollback button
- [ ] 6 minggu live — validate edge improvement

### P3 — Tier 2 Suggest-Only
- [ ] Pending suggestion data model
- [ ] Frontend: suggestion card UI dengan Approve/Dismiss/Snooze
- [ ] Backend: track approval rate → calibrate Tier 1 vs Tier 2 confidence
- [ ] 1 bulan suggest-only mode

### P4 — Bayesian (future)
- [ ] Tracker — kembali setelah 500+ trade

---

## 12. Open Decisions (TBD)

Pertanyaan yang harus user jawab sebelum / saat coding:

1. **Rolling window default size**: 30 trade (recommended) atau lebih panjang (50/100)?
2. **Statistical test**: Wilson interval (recommended, sederhana) atau Bayesian beta posterior (lebih akurat, butuh prior)?
3. **Auto re-enable timer**: session skip 7 hari OK, atau lebih konservatif (14 hari)?
4. **Audit history retention**: simpan 50 atau 200 entries?
5. **Tier 2 timing**: aktifkan setelah 80 trade dengan Tier 1 (cepat) atau 200 trade (lebih konservatif)?

Default rekomendasi: 30, Wilson, 7 hari, 50 entries, 80 trade. User boleh redirect kapan saja.

---

## 13. Related Documents

- [TRADING_FEATURES.md](TRADING_FEATURES.md) — current feature catalog + status
- [api-reference.md](api-reference.md) — REST endpoints (akan ada `/api/adaptive/*` baru)
- [architecture.md](architecture.md) — overall system architecture
- `data/{mode}/system-state.json` — current static config (akan reference adaptive-state.json)
