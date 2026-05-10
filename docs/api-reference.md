# API Reference

Base URL (development): `http://localhost:5000`  
Swagger UI: `http://localhost:5000/swagger`

All requests and responses use **camelCase JSON**. Enum values are serialized as **strings** (e.g., `"BUY"`, `"ACTIVE"`).

---

## Endpoints

### POST /api/signal/analyze

Analyze market conditions and generate a trade signal.

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
  "runId": "run-20260510-001",
  "pair": "EUR/USD",
  "timeframe": "M15",
  "signal": "BUY",
  "confluenceScore": 72,
  "confidenceScore": 0.69,
  "snapshot": {
    "pair": "EUR/USD",
    "currentPrice": 1.1268,
    "ma20M15": 1.1251,
    "ma50M15": 1.1230,
    "rsi14": 58.4,
    "rsiDirection": "rising",
    "session": "London"
  },
  "trendAnalysis": { "bias": "BULLISH", "strength": "STRONG", "score": 0.78, "htfAligned": true },
  "momentumAnalysis": { "rsiValue": 58.4, "rsiDirection": "rising", "zone": "NEUTRAL", "score": 0.65 },
  "structureAnalysis": { "nearestSupport": "1.1240-1.1250", "score": 0.70, "candleConfirmed": true },
  "tradeParameters": {
    "entry": 1.1268, "stopLoss": 1.1244, "stopLossPips": 24,
    "takeProfit": 1.1313, "takeProfitPips": 45,
    "lotSize": 0.41, "riskAmount": 100.0, "potentialProfit": 187.5, "riskRewardRatio": 1.87
  },
  "timestamp": "2026-05-10T14:09:05+00:00",
  "warnings": []
}
```

---

### POST /api/risk/evaluate

Evaluate risk for a signal using a predictor result.

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
    "entry": 1.1268, "stopLoss": 1.1244, "stopLossPips": 24,
    "takeProfit": 1.1313, "takeProfitPips": 45,
    "lotSize": 0.41, "riskAmount": 100.0, "potentialProfit": 187.5, "riskRewardRatio": 1.87
  },
  "cautionNotes": ["Confidence 69% — above minimum but below strong threshold of 70%"],
  "noGoReasons": []
}
```

**Risk gate logic (in order):**

| Condition | Decision |
|-----------|----------|
| `signal == HOLD` | `NO-GO` |
| `adjustedConfidence < 0.60` | `NO-GO` |
| `openPositions >= 3` | `NO-GO` |
| `adjustedConfidence < 0.70` | `GO_WITH_CAUTION` |
| otherwise | `GO` |

---

### POST /api/trade/execute

Execute a trade given a risk validation result.

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
  "tradeId": "SIM-20260510-140905",
  "runId": "run-20260510-001",
  "status": "ACTIVE",
  "pair": "EUR/USD",
  "direction": "BUY",
  "entry": 1.1268,
  "stopLoss": 1.1244,
  "takeProfit": 1.1313,
  "lotSize": 0.41,
  "riskAmount": 100.0,
  "potentialProfit": 187.5,
  "riskReward": 1.87,
  "floatingPnl": 0.0,
  "floatingPnlPips": 0,
  "openedAt": "2026-05-10T14:09:05+00:00",
  "closedAt": null,
  "mode": "SIMULATION"
}
```

**Hard limits enforced before execution:**

| Limit | Value | Action |
|-------|-------|--------|
| Risk per trade | max **1%** equity | Reject if `riskAmount > equity * 0.01` |
| Max drawdown | **10%** | Reject if `currentEquity < peakEquity * 0.90` |
| Max open positions | **3** | Reject if `countOpen >= 3` |
| AI confidence | min **60%** | Rejected at risk gate, but double-checked |
| Risk decision | must be `GO` or `GO_WITH_CAUTION` | Reject if `NO-GO` |

---

### GET /api/position/{pair}

Get the active position for a currency pair.

**Parameters**

| Name | In | Description |
|------|----|-------------|
| `pair` | path | Pair identifier — `EURUSD` or `EUR/USD` (normalized) |

**Response 200** — returns same `TradePosition` shape as execute endpoint.

**Response 204** — no active position for this pair.

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

**500 Internal Server Error**
```json
{
  "error": "An unexpected error occurred"
}
```

> Stack traces are never exposed in responses.

---

## Running the API

```bash
dotnet run --project src/ForexAI.API
# → http://localhost:5000/swagger
```

### Prerequisites

Stub services read from BMAD planning artifact files. These must exist before the API starts:

```
_bmad-output/planning-artifacts/signal-output.json
_bmad-output/planning-artifacts/risk-decision.json
```

Run `/forex-market-analysis-signal` and `/forex-risk-management-gate` BMAD skills to generate them.
