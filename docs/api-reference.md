# API Reference

Base URL (development): `http://localhost:8080`
Swagger UI: `http://localhost:8080/swagger`

Semua request/response **camelCase JSON**. Enum di-serialize sebagai **string** (e.g. `"BUY"`, `"ACTIVE"`).

**Sumber data:** MT5 MIFX Expert Advisor (push tick + candle + position report). Tanpa EA running, endpoint signal/market akan return 503 (`InvalidOperationException: EA not connected`).

---

## Signal Analysis

### POST /api/signal/analyze

Generate sinyal dari live market data.

**Request**
```json
{
  "pair": "EURUSD",
  "timeframe": "M15"
}
```

**Response 200**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "pair": "EUR/USD",
  "timeframe": "M15",
  "signal": "BUY",
  "confluenceScore": 72,
  "confidenceScore": 0.69,
  "snapshot": {
    "pair": "EUR/USD",
    "currentPrice": 1.0850,
    "ma20M15": 1.0840,
    "ma50M15": 1.0820,
    "ma20H1": 1.0830,
    "ma50H1": 1.0800,
    "rsi14": 58.4,
    "rsiDirection": "rising",
    "session": "LONDON",
    "atr14": 0.0008,
    "adx14": 28,
    "regime": "Trending"
  },
  "trendAnalysis": { "bias": "BULLISH", "strength": "STRONG", "score": 0.78, "htfAligned": true },
  "momentumAnalysis": { "rsiValue": 58.4, "rsiDirection": "rising", "zone": "NEUTRAL", "score": 0.65 },
  "structureAnalysis": { "nearestSupport": "1.0820-1.0830", "score": 0.70 },
  "tradeParameters": {
    "entry": 1.0850, "stopLoss": 1.0830, "stopLossPips": 20,
    "takeProfit": 1.0880, "takeProfitPips": 30,
    "lotSize": 0.41, "riskAmount": 100.0, "potentialProfit": 150.0, "riskRewardRatio": 1.50
  },
  "timestamp": "2026-05-16T14:09:05+00:00",
  "warnings": []
}
```

**Response 503** — EA belum push tick pertama atau di-disconnect.

---

## Risk Evaluation

### POST /api/risk/evaluate

Evaluate signal terhadap hard risk invariants.

**Request**
```json
{
  "signalId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "finalDecision": "BUY",
  "adjustedConfidence": 0.69,
  "totalScore": 83,
  "agreementScore": 0.93,
  "equity": 10000.00,
  "openPositions": 0
}
```

**Response 200**
```json
{
  "decision": "GO_WITH_CAUTION",
  "positionDecision": "OPEN",
  "isGo": true,
  "validatedParameters": {
    "entry": 1.0850, "stopLoss": 1.0830, "stopLossPips": 20,
    "takeProfit": 1.0880, "takeProfitPips": 30,
    "lotSize": 0.41, "riskAmount": 100.0, "potentialProfit": 150.0, "riskRewardRatio": 1.50
  },
  "cautionNotes": ["Confidence 69% — above minimum but below strong threshold of 70%"],
  "noGoReasons": []
}
```

**Decision logic (in order):**

| Condition | Decision |
|---|---|
| `signal == HOLD` | `NO_GO` |
| `adjustedConfidence < 0.60` | `NO_GO` |
| `openPositions >= 3` | `NO_GO` |
| `drawdown >= 10%` | `NO_GO` (system STOP) |
| `dailyTradeCount >= 7` | `NO_GO` |
| `adjustedConfidence < 0.70` | `GO_WITH_CAUTION` |
| otherwise | `GO` |

---

## Trade Execution

### POST /api/trade/execute

Execute trade berdasarkan risk validation. Kalau `Mifx:EnableExecution = true`, kirim order ke EA. Kalau false, simulate.

**Request**
```json
{
  "signalId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "riskValidation": {
    "decision": "GO_WITH_CAUTION",
    "positionDecision": "OPEN",
    "isGo": true,
    "validatedParameters": { ... },
    "cautionNotes": ["..."],
    "noGoReasons": []
  },
  "peakEquity": 10000.00,
  "currentEquity": 10000.00,
  "mode": "SIMULATION"
}
```

**Response 200**
```json
{
  "tradeId": "SIM-20260516-140905",
  "status": "ACTIVE",
  "pair": "EUR/USD",
  "direction": "BUY",
  "entry": 1.0850,
  "stopLoss": 1.0830,
  "takeProfit": 1.0880,
  "lotSize": 0.41,
  "riskAmount": 100.0,
  "floatingPnl": 0.0,
  "openedAt": "2026-05-16T14:09:05+00:00",
  "closedAt": null,
  "mode": "SIMULATION"
}
```

**Final guards (sebelum kirim order):**

| Limit | Action |
|---|---|
| `riskAmount > equity * 0.01` | Reject |
| `currentEquity < peakEquity * 0.90` | Reject (DD STOP) |
| `countOpen >= 3` | Reject |
| Decision `NO_GO` | Reject |
| `SystemStateService.IsHalted` | Reject |

Hard-fail → `TradePosition` dengan `status: SKIPPED` + `skipReason`.

---

## Positions

### GET /api/position

List semua posisi (`ACTIVE` + closed). Query param `status` untuk filter.

### GET /api/position/{pair}

Active position untuk pair tertentu. `pair` boleh `EURUSD` atau `EUR/USD`.

**Response 200** — `TradePosition` shape sama dengan execute response.
**Response 204** — no active position.

### POST /api/position/{tradeId}/close

Close position via broker (atau simulasi).

**Request**
```json
{ "outcome": "WIN", "exitPrice": 1.0880 }
```

**Response 200** — `TradePosition` dengan `status: CLOSED_WIN` atau `CLOSED_LOSS`, `closedAt`, `realizedPnl`.

---

## Account

### GET /api/account

**Response 200**
```json
{
  "balance": 10000.00,
  "equity": 10125.50,
  "peakEquity": 10200.00,
  "drawdownPct": 0.73,
  "openPositions": 1,
  "mode": "DEMO",
  "isHalted": false
}
```

---

## Backtest

### POST /api/backtest/run

Replay signal-history.json untuk evaluate strategy changes.

**Request**
```json
{ "from": "2026-04-01", "to": "2026-05-01" }
```

**Response 200** — summary statistics (winrate, total trades, expectancy, max DD, profit factor).

---

## Audit

### GET /api/audit?limit=100

Tail audit log dari `data/{mode}/audit-log.jsonl`.

**Response 200** — array of audit events (newest first).

---

## EA Deploy

### POST /api/ea/deploy

Copy + compile MQL5 EA ke MT5 terminal folder.

**Response 200**
```json
{ "deployed": true, "terminalPath": "/Users/.../Terminal/<hash>", "compiled": true }
```

---

## System

### GET /api/system/status

```json
{
  "isHalted": false,
  "haltReason": null,
  "lastEaTick": "2026-05-16T14:09:00+00:00",
  "uptime": "PT2H15M",
  "version": "1.18"
}
```

---

## Market

### GET /api/market/snapshot?pair=EURUSD&timeframe=M15

Current `MarketSnapshot` (bypass signal analysis — raw indicators only).

---

## MIFX Bridge (EA inbound)

`POST /api/mifx-bridge/tick`, `/api/mifx-bridge/candle`, `/api/mifx-bridge/position`, `/api/mifx-bridge/account` — endpoint yang di-call dari MT5 EA. Payload contract di [mql5/ForexAI_Bridge.mq5](../mql5/ForexAI_Bridge.mq5).

---

## SignalR

Hub: `ws://localhost:8080/hub/dashboard` (atau https + wss kalau dideploy).

**Server → client events:**
- `PositionUpdate` — payload `TradePosition`
- `SignalGenerated` — payload `TradeSignal`
- `AccountUpdate` — payload account snapshot
- `SystemStateChanged` — payload `{isHalted, reason}`

Frontend pakai `@microsoft/signalr` client (lihat [frontend/src/lib/](../frontend/src/lib/)).

---

## Error Responses

**400 Bad Request** — validation failure
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "errors": { "pair": ["The pair field is required."] }
}
```

**500 Internal Server Error / 503 Service Unavailable**
```json
{ "error": "EA not connected" }
```

503 untuk known domain error (`InvalidOperationException` — biasanya EA disconnect). 500 untuk unexpected. Stack trace tidak pernah ke-expose.
