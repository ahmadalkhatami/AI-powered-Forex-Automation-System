# AI-powered Forex Automation System

> Semi-automated EUR/USD trading system. Live MT5 data → rule-based signal analysis → risk gate → human-approval dashboard → broker execution via MIFX Expert Advisor.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Next.js](https://img.shields.io/badge/Next.js-14-000?logo=nextdotjs)
![MT5](https://img.shields.io/badge/MetaTrader-5-1A73E8)
![Mode](https://img.shields.io/badge/default-SIMULATION-orange)
![License](https://img.shields.io/badge/license-MIT-green)

---

## What it does

5-stage pipeline untuk setiap trade decision:

```
Market Data  →  Signal Analysis  →  Risk Gate  →  Human Approval  →  Execution
(MIFX EA)       (LiveSignalAnalyzer)  (RuleBased   (Dashboard)        (MIFX EA
 tick+candle     MA/RSI/S-R + adaptive  Evaluator)                     order)
                 mode + HTF D1 veto)
```

1. **MIFX EA** push live tick + candle (M15/H1/D1) + account info ke backend via REST.
2. **LiveSignalAnalyzer** menghitung MA crossover, RSI, S/R zone, ATR/ADX regime — output sinyal BUY/SELL/HOLD + confidence 0–100.
3. **RuleBasedRiskEvaluator** enforce invariant: 1% risk/trade, max DD 10%, max 3 posisi, min confidence 60.
4. **Dashboard (Next.js)** menampilkan sinyal + risk decision; user approve/reject — atau auto-approve di confidence ≥ 70%.
5. **MifxBrokerService** kirim order ke EA via command queue; EA eksekusi di MetaTrader 5.

Default mode: **simulation**. Real money execution di-gate oleh `MifxSettings.EnableExecution` + auto-detection mode dari `AccountInfoString` (REAL vs DEMO).

---

## Architecture

```
                  ┌───────────────────────────────┐
                  │    ForexAI.API (port 8080)    │
                  │  Controllers + SignalR Hub    │
                  └───────────┬───────────────────┘
                              │
              ┌───────────────▼───────────────┐
              │     ForexAI.Application       │
              │  AnalyzeSignal · EvaluateRisk │
              │  ExecuteTrade · ClosePosition │
              │  GetAccountHealth · ...       │
              └───────────────┬───────────────┘
                              │
              ┌───────────────▼─────────────────────────────┐
              │           ForexAI.Domain                    │
              │  TradePosition · TradeSignal · MarketSnap   │
              │  ISignalAnalyzer · IRiskEvaluator · ...     │
              └───────────────┬─────────────────────────────┘
                              │
              ┌───────────────▼─────────────────────────────┐
              │       ForexAI.Infrastructure                │
              │  LiveSignalAnalyzer · RuleBasedRiskEval     │
              │  Mifx bridge (EA ⇄ API) · Json repos        │
              │  BacktestRunner · EaDeployService           │
              └─────────────────────────────────────────────┘
```

Clean Architecture — dependency arrows point inward. Domain pure, no IO/framework refs.

Frontend (`frontend/`) adalah Next.js 14 dashboard di port 3000, terhubung ke API via REST + SignalR hub (`/hub/dashboard`).

EA (`mql5/ForexAI_Bridge.mq5`) berjalan di MetaTrader 5 dan push data ke `/api/mifx-bridge/*`.

---

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- MetaTrader 5 dengan MIFX account (Windows / wine) — _optional untuk simulasi_

### Run

```bash
git clone <repo> && cd AI-powered-Forex-Automation-System

# 1. Build solution
dotnet build ForexAI.sln

# 2. Start backend API → http://localhost:8080 (Swagger: /swagger)
dotnet run --project src/ForexAI.API

# 3. Start frontend → http://localhost:3000
cd frontend && npm install && npm run dev
```

Tanpa MT5 EA running, signal analysis akan return error (`InvalidOperationException: EA not connected`). Untuk smoke test, jalankan integration test:

```bash
dotnet test ForexAI.sln
```

Test pakai `FakeMarketDataService` yang return deterministic bullish setup — tidak perlu broker EA.

---

## Hard Risk Invariants

Enforced di [src/ForexAI.Infrastructure/Services/RuleBasedRiskEvaluator.cs](src/ForexAI.Infrastructure/Services/RuleBasedRiskEvaluator.cs). Tidak bisa di-bypass via API.

| Rule | Value |
|------|-------|
| Risk per trade | **1%** equity (Nano tier ada $ cap tambahan) |
| Max drawdown | **10%** → sistem auto-STOP |
| Max posisi terbuka | **3** |
| Min confidence | **60** (di bawah ini = auto NO-GO) |
| Auto-approve threshold | **≥ 70%** confidence |
| Max trade/hari | **7** |

---

## Repository Structure

```
src/
├── ForexAI.Domain/          # Pure entities, value objects, interfaces
├── ForexAI.Application/     # Use cases (no infra dependency)
├── ForexAI.Infrastructure/  # MIFX EA bridge, JSON repos, broker adapters
└── ForexAI.API/             # ASP.NET Core controllers + SignalR hub

frontend/                    # Next.js 14 dashboard (TS + Tailwind)
mql5/                        # MT5 Expert Advisor source + compiled .ex5
tests/ForexAI.Integration/   # xUnit + WebApplicationFactory
data/                        # Runtime data (mostly gitignored)
├── mode-state.json          # Current trading mode (demo|real)
├── demo/                    # Demo-mode trade history & cache
└── real/                    # Real-mode (kosong sampai mode flip)

docs/                        # Architecture, API, pipeline, dev docs
└── history/                 # Story specs Epic 1-4 (historical reference)
```

---

## Docs

- [Architecture](docs/architecture.md) — Clean Architecture layers + adapter mapping
- [Pipeline](docs/pipeline.md) — Stage-by-stage signal/risk/execution flow
- [API Reference](docs/api-reference.md) — REST endpoint specs
- [Development Guide](docs/development.md) — Setup, EA deployment, debugging

---

## License

MIT
