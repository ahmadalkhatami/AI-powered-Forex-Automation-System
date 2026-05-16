# Trading Pipeline

Pipeline 5-stage human-in-the-loop. Sumber data: MT5 Expert Advisor push live tick + candle + account ke API. Eksekusi balik via MIFX EA command queue.

---

## Overview

```
┌──────────────────────────────────────────────────────────────────┐
│  MT5 / MIFX EA  ──tick + candle (M15/H1/D1) + account──▶ API     │
└──────────────────────────────┬───────────────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────────────┐
│  Stage 1: POST /api/signal/analyze                                │
│    → LiveSignalAnalyzer (MA + RSI + S/R + ATR + ADX + HTF veto)   │
│    → output: TradeSignal {direction, confidence, params}           │
│                                                                   │
│  Stage 2: POST /api/risk/evaluate                                 │
│    → RuleBasedRiskEvaluator (1%/DD 10%/max 3 pos/min conf 60)     │
│    → output: RiskValidation {GO | NO_GO | GO_WITH_CAUTION}        │
│                                                                   │
│  Stage 3: Human approval (atau auto di confidence ≥ 70%)         │
│                                                                   │
│  Stage 4: POST /api/trade/execute                                 │
│    → ExecuteTradeHandler: final guard + create TradePosition       │
│    → MifxBrokerService → MifxCommandQueue → EA  (atau simulasi)   │
│                                                                   │
│  Stage 5: GET /api/position/{pair}  +  SignalR push               │
│    → MifxPositionSyncService update dari EA report                 │
└──────────────────────────────────────────────────────────────────┘
```

Setiap stage persist ke `data/{mode}/`: signal history, execution log, position status, audit jsonl.

---

## Stage 1 — Signal Analysis

**Endpoint:** `POST /api/signal/analyze`
**Service:** [`LiveSignalAnalyzer`](../src/ForexAI.Infrastructure/Services/LiveSignalAnalyzer.cs)
**Input source:** `MifxFullDataService` (current tick + indicators dari EA payload) + `MifxCandleDataService` (D1 candles untuk HTF veto)

Algoritma:

| Komponen | Detail |
|---|---|
| **Trend** | MA20 vs MA50 crossover di M15 dan H1. HTF alignment (M15 ↔ H1 ↔ D1) memberi confidence bonus. |
| **Momentum** | RSI(14) dengan direction (rising/falling) dan zone (oversold < 30, neutral, overbought > 70). |
| **Structure** | Jarak ke nearest support / resistance zone. Trade di-block kalau price terlalu dekat ke zona berlawanan arah sinyal. |
| **Volatility** | ATR(14) M15 untuk dynamic SL/TP sizing. Min SL 20 pip (MIFX stop level). |
| **Regime** | ADX(14) M15 + price action: `Trending` / `Ranging` / `Volatile` / `Transitional`. Filter berbeda per regime. |
| **HTF D1 veto** | Kalau D1 trend (SMA20 vs SMA50) berlawanan arah sinyal M15 → reject. |
| **Adaptive mode** | Demo: aggressive params untuk explore. Real: konservatif + Nano tier $ caps. Auto-detect dari `ModeService`. |
| **Filter stack** | Setup vetos (avoid news window, time stop, daily max trade), dual filter (confidence + confluence), session check. |

Output: `TradeSignal` dengan `direction` (BUY/SELL/HOLD), `confidence` (0–100), `confluenceScore`, `tradeParameters` (entry, SL, TP, lot suggestion).

Persisted ke `data/{mode}/signal-history.json` via `JsonSignalRepository`.

---

## Stage 2 — Risk Gate

**Endpoint:** `POST /api/risk/evaluate`
**Service:** [`RuleBasedRiskEvaluator`](../src/ForexAI.Infrastructure/Services/RuleBasedRiskEvaluator.cs)

Pure rule evaluation:

```
signal == HOLD?              → NO_GO  (no actionable direction)
confidence < 60?             → NO_GO  (below threshold)
openPositions >= 3?          → NO_GO  (max concurrent reached)
drawdown >= 10%?             → NO_GO  (system STOP)
daily trade count >= 7?      → NO_GO  (throttle)
confidence < 70?             → GO_WITH_CAUTION (manual approval)
else                         → GO  (auto-approve eligible)
```

Output: `RiskValidation` dengan `decision`, `validatedParameters` (final lot setelah position sizing), `cautionNotes`.

---

## Stage 3 — Approval

Frontend dashboard menampilkan signal + risk decision. Trader review:
- Signal direction + confluence breakdown
- Risk/reward + final lot
- Setup vetos (kenapa GO_WITH_CAUTION)

User approve → call `/api/trade/execute`. Reject → signal di-mark `REJECTED_BY_USER` di history.

**Auto-approve**: confidence ≥ 70% dengan decision `GO` di-execute otomatis tanpa human gate (configurable).

---

## Stage 4 — Execution

**Endpoint:** `POST /api/trade/execute`
**Handler:** `ExecuteTradeHandler` (di [src/ForexAI.Application/UseCases/ExecuteTrade/](../src/ForexAI.Application/UseCases/ExecuteTrade/))

Final guards sebelum kirim order:

| Check | Limit |
|---|---|
| Drawdown | `currentEquity >= peakEquity × 0.90` |
| Open positions | `< 3` |
| Risk amount | `<= equity × 1%` (plus Nano $ cap kalau real mode) |
| Decision | `GO` atau `GO_WITH_CAUTION` |
| System STOP | `SystemStateService.IsHalted == false` |

Jika lulus:
- Construct `TradeParameters` (direction, entry, lot, SL, TP)
- Create `TradePosition` dengan `status: ACTIVE`
- `MifxBrokerService.PlaceOrderAsync()` enqueue command ke `MifxCommandQueue`
- EA poll queue, eksekusi di MT5, kirim confirmation back
- Persist ke `data/{mode}/execution-log.json` + audit log

Jika gagal: `TradePosition` dengan `status: SKIPPED` + `skipReason`.

---

## Stage 5 — Position Monitoring

**Endpoint:** `GET /api/position/{pair}` (snapshot) + SignalR hub `/hub/dashboard` (push).

`MifxPositionSyncService` listen EA position report, update repo, broadcast ke dashboard.

Live PnL di-interpolate frontend antara EA tick (lihat commit `698b1a7 feat(dashboard): live PnL interpolation antara broker tick`).

Close path: `POST /api/position/{id}/close` → `ClosePositionHandler` → `MifxBrokerService.ClosePositionAsync()` → EA close → update status `CLOSED_WIN` / `CLOSED_LOSS`.

---

## Simulation vs Real

Mode di-detect otomatis oleh `ModeService` dari EA `AccountInfoString`:
- `"REAL"` → `TradeMode.Real` → `data/real/`
- Lain (`DEMO`, `CONTEST`, null) → `TradeMode.Demo` → `data/demo/`

`Mifx:EnableExecution` di [appsettings.json](../src/ForexAI.API/appsettings.json) — kalau `false`, `IBrokerService` resolve ke `NullBrokerService` (pure simulasi, tidak kirim order ke EA).

**Hard $ caps untuk real money Nano tier** (lihat commit `2bcc052`): tambahan kappar di luar 1% risk untuk safety di akun kecil real.

---

## Audit Trail

Setiap stage transition di-log ke `data/{mode}/audit-log.jsonl` (append-only JSON lines) oleh `AuditLogger`. Format:

```json
{"timestamp":"2026-05-16T13:24:00Z","event":"SIGNAL_GENERATED","signalId":"sig_...","data":{...}}
{"timestamp":"2026-05-16T13:24:01Z","event":"RISK_EVALUATED","signalId":"sig_...","decision":"GO_WITH_CAUTION"}
{"timestamp":"2026-05-16T13:24:30Z","event":"TRADE_EXECUTED","tradeId":"trd_...","status":"ACTIVE"}
```

Audit log di-rotate runtime (gitignored).
