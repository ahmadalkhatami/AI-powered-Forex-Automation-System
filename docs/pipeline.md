# Trading Pipeline

The ForexAI pipeline is a **sequential, human-in-the-loop** process. Each stage produces a JSON artifact consumed by the next. The frontend dashboard (Epic 2–4) orchestrates the stages via REST API calls.

---

## Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                      BMAD AI Skills Layer                        │
│                                                                   │
│  /forex-market-analysis-signal   /forex-risk-management-gate     │
│           ↓                               ↓                       │
│  signal-output.json              risk-decision.json               │
└───────────────────────┬─────────────────────┬────────────────────┘
                        │  Planning Artifacts  │
                        ↓                      ↓
┌─────────────────────────────────────────────────────────────────┐
│                    REST API Pipeline                              │
│                                                                   │
│  POST /api/signal/analyze                                         │
│       ↓  TradeSignal (id, BUY/SELL/HOLD, confidence, params)     │
│                                                                   │
│  POST /api/risk/evaluate                                          │
│       ↓  RiskValidation (GO / NO-GO / GO_WITH_CAUTION)           │
│                                                                   │
│  POST /api/trade/execute         ←── Human approves GO decision  │
│       ↓  TradePosition (ACTIVE / SKIPPED)                        │
│                                                                   │
│  GET  /api/position/{pair}       ←── Monitor active position     │
└─────────────────────────────────────────────────────────────────┘
                        │
                        ↓
              execution-log.json
```

---

## Stage 1 — Market Analysis Signal

**Invocation:** BMAD skill `/forex-market-analysis-signal`  
**Agent:** Farida 📈 (`forex-agent-market-analyst`)  
**Output:** `_bmad-output/planning-artifacts/signal-output.json`

Farida analyzes EUR/USD on M15 + H1 timeframes using:
- **Trend:** MA20/MA50 crossover on both timeframes — HTF alignment bonus
- **Momentum:** RSI14 direction and zone (oversold/neutral/overbought)
- **Structure:** Nearest support/resistance, candle pattern confirmation

Output contains `signal: BUY | SELL | HOLD`, `confluence_score`, and pre-risk `trade_parameters`.

---

## Stage 2 — AI Predictor Validation

**Agent:** Zara 🤖 (`forex-agent-ai-predictor`)  
**Reads:** `signal-output.json`  
**Output:** `PredictorResult` embedded in the API request body

Zara validates the signal using rule-based + LLM cross-checking:
- Produces `adjusted_confidence` (0.0–1.0) and `agreement_score`
- Generates `validation_notes` for any concerns
- Returns `final_decision: BUY | SELL | HOLD`

> Currently stateless — invoked inline before calling `/api/risk/evaluate`.

---

## Stage 3 — Risk Gate

**API endpoint:** `POST /api/risk/evaluate`  
**Service:** `RuleBasedRiskEvaluator`

Pure rule evaluation against hard system limits:

```
signal == HOLD?          → NO-GO  (no actionable direction)
confidence < 60%?        → NO-GO  (AI certainty too low)
open positions >= 3?     → NO-GO  (max concurrent trades reached)
confidence < 70%?        → GO_WITH_CAUTION
else                     → GO
```

Returns `RiskValidation` with `decision`, `validatedParameters` (with lot sizing), and `cautionNotes`.

---

## Stage 4 — Human Approval

The frontend dashboard presents the risk validation result to the trader. The trader reviews:
- Signal direction and confluence score
- Risk/reward ratio and lot size
- Risk gate decision and caution notes

If the trader clicks **Approve**, the dashboard calls `/api/trade/execute`.

---

## Stage 5 — Trade Execution

**API endpoint:** `POST /api/trade/execute`  
**Handler:** `ExecuteTradeHandler`

Final hard-limit checks before creating the position:

| Check | Limit |
|-------|-------|
| Drawdown guard | `currentEquity >= peakEquity × 0.90` |
| Max positions | `openPositions < 3` |
| Risk amount | `riskAmount <= equity × 0.01` |
| Decision | must be `GO` or `GO_WITH_CAUTION` |

On pass → creates `TradePosition` with `status: ACTIVE`, persists to `execution-log.json`.  
On fail → creates `TradePosition` with `status: SKIPPED` and `skipReason`.

---

## Stage 6 — Position Monitoring

**API endpoint:** `GET /api/position/{pair}`

Returns the active position for real-time PnL display in the dashboard.  
Returns `204 No Content` when no active position exists.

---

## Data Flow Summary

```
signal-output.json ──→ StubMarketDataService ──→ AnalyzeSignalHandler
risk-decision.json ──→ BmadSignalAnalyzer    ──→   (trade parameters)
                                                          ↓
                   Frontend sends PredictorResult ──→ EvaluateRiskHandler
                                                          ↓
                   Frontend sends RiskValidation  ──→ ExecuteTradeHandler
                                                          ↓
                                               execution-log.json
```

---

## Simulation vs Live

All trades currently run in `SIMULATION` mode. Trade IDs are prefixed `SIM-`.  
Live execution requires MT5 bridge (Windows-only) or OANDA REST API.  
See `skills/forex-agent-execution/references/mt5-bridge.md` for the integration plan.
