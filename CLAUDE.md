# CLAUDE.md

Guidance untuk Claude Code saat bekerja di repo ini.

## Project Overview

**AI-powered Forex Automation System** — semi-automated trading EUR/USD pada M15/H1, simulation-first, dengan human-approval dashboard. Backend **.NET 8 Clean Architecture**, frontend **Next.js**, eksekusi via **MIFX MT5 Expert Advisor bridge**.

Sinyal di-generate sepenuhnya di kode (`LiveSignalAnalyzer`) dari live tick + candle yang di-push EA. Tidak ada lagi pipeline AI agent eksternal.

## Communication

- **Bahasa:** Indonesia (kecuali code/log/identifier teknis).
- **User:** AhmadAlkhatami (intermediate).

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│  MIFX MT5 EA  ──tick / candle / account──▶  REST + SignalR
│                                                          │
│  ForexAI.API (Kestrel http://localhost:8080)             │
│   ├─ Controllers     (REST endpoints + SignalR hub)      │
│   └─ DashboardHub    (real-time push ke frontend)        │
│                                                          │
│  ForexAI.Application  (UseCases: AnalyzeSignal,          │
│                        EvaluateRisk, ExecuteTrade, ...)  │
│                                                          │
│  ForexAI.Domain       (Entities + ValueObjects +         │
│                        Interfaces — pure C#)             │
│                                                          │
│  ForexAI.Infrastructure                                  │
│   ├─ Services/        LiveSignalAnalyzer (MA+RSI+S/R     │
│   │                   + adaptive mode + HTF D1 veto      │
│   │                   + filter stack)                    │
│   │                   RuleBasedRiskEvaluator             │
│   │                   BacktestRunner, EaDeployService    │
│   ├─ Mifx/            Bridge ke EA (tick feed, candle    │
│   │                   feed, command queue, position sync)│
│   ├─ Broker/          MifxBrokerService, NullBroker      │
│   ├─ Persistence/     JsonTradePositionRepository,       │
│   │                   JsonSignalRepository (file-backed) │
│   └─ Services/Deriv,  Alternative broker adapters        │
│      Services/Exness, (not active in default DI)         │
│      Services/AI                                         │
│                                                          │
│  frontend/  (Next.js 14, port 3000)                      │
│   └─ Dashboard: live price chart, position card,         │
│                signal panel, risk gate, trade history    │
└──────────────────────────────────────────────────────────┘
```

## Hard Risk Invariants (jangan di-bypass tanpa diskusi)

- **Risk per trade:** 1% equity (Nano tier punya $ cap tambahan untuk real money)
- **Max drawdown:** 10% → sistem auto-STOP
- **Max posisi terbuka:** 3
- **Min confidence:** 60 → di bawah ini = auto NO-GO
- **Auto-approve threshold:** confidence ≥ 70%
- **Max trade/hari:** 7

Implementasi: [src/ForexAI.Infrastructure/Services/RuleBasedRiskEvaluator.cs](src/ForexAI.Infrastructure/Services/RuleBasedRiskEvaluator.cs)

## Folder Layout

```
src/
├── ForexAI.Domain/             # Entities, ValueObjects, Interfaces (no deps)
├── ForexAI.Application/        # UseCases (AnalyzeSignal, EvaluateRisk, ExecuteTrade, ...)
├── ForexAI.Infrastructure/     # Adapters: Mifx EA bridge, Broker, Persistence, Services
└── ForexAI.API/                # ASP.NET Core: Controllers, SignalR Hubs, Program.cs

frontend/                       # Next.js dashboard (TS + Tailwind)
mql5/                           # MetaTrader 5 Expert Advisor: ForexAI_Bridge.mq5
tests/ForexAI.Integration/      # Integration tests (xUnit + WebApplicationFactory)
data/                           # Runtime data (mostly gitignored)
├── mode-state.json             # Current trading mode (demo|real)
├── demo/                       # Demo-mode trade history
│   ├── audit-log.jsonl
│   ├── execution-log.json
│   ├── position-status.json
│   ├── signal-history.json
│   ├── mifx-candle-cache.json
│   └── system-state.json
└── real/                       # Real-mode (kosong sampai mode flip)

docs/                           # Architecture, API ref, pipeline, development docs
└── history/                    # Story specs Epic 1-4 + sprint status (historical)
```

## Running

```bash
# 1. Backend API (http://localhost:8080)
dotnet build ForexAI.sln
dotnet run --project src/ForexAI.API

# 2. Frontend dashboard (http://localhost:3000)
cd frontend && npm install && npm run dev

# 3. Integration tests
dotnet test ForexAI.sln
```

MT5 EA harus running di MetaTrader 5 (Windows / wine) untuk live market data dan eksekusi. Lihat [docs/development.md](docs/development.md) untuk setup EA.

## Key Services (Infrastructure Layer)

| Service | File | Role |
|---------|------|------|
| `LiveSignalAnalyzer` | [Services/LiveSignalAnalyzer.cs](src/ForexAI.Infrastructure/Services/LiveSignalAnalyzer.cs) | Generate sinyal BUY/SELL/HOLD dari MA + RSI + S/R + ATR + ADX, dengan adaptive mode (demo vs real tier) dan HTF D1 veto |
| `RuleBasedRiskEvaluator` | [Services/RuleBasedRiskEvaluator.cs](src/ForexAI.Infrastructure/Services/RuleBasedRiskEvaluator.cs) | Risk gate: enforce 1% risk, max DD 10%, max 3 posisi, min confidence 60 |
| `MifxFullDataService` | [Mifx/MifxFullDataService.cs](src/ForexAI.Infrastructure/Mifx/MifxFullDataService.cs) | `IMarketDataService` — baca tick payload lengkap (MA/RSI/S-R) dari EA |
| `MifxCandleDataService` | [Mifx/MifxCandleDataService.cs](src/ForexAI.Infrastructure/Mifx/MifxCandleDataService.cs) | `ICandleDataService` — candle M15/H1/D1 dari EA push |
| `MifxBrokerService` | [Mifx/MifxBrokerService.cs](src/ForexAI.Infrastructure/Mifx/MifxBrokerService.cs) | `IBrokerService` — kirim order/close via command queue ke EA |
| `MifxPositionSyncService` | [Mifx/MifxPositionSyncService.cs](src/ForexAI.Infrastructure/Mifx/MifxPositionSyncService.cs) | Real-time sync posisi dari EA report |
| `ModeService` | [ModeService.cs](src/ForexAI.Infrastructure/ModeService.cs) | Auto-detect demo/real dari EA `AccountInfoString` |
| `AuditLogger` | Infrastructure/Services | Append-only log ke `data/{mode}/audit-log.jsonl` |
| `BacktestRunner` | [Services/BacktestRunner.cs](src/ForexAI.Infrastructure/Services/BacktestRunner.cs) | Replay signal-history.json untuk backtest strategi |

Default DI wiring di [src/ForexAI.Infrastructure/DependencyInjection.cs](src/ForexAI.Infrastructure/DependencyInjection.cs).

## REST Endpoints (ringkas)

Base: `http://localhost:8080`, Swagger UI: `/swagger`.

| Endpoint | Controller |
|----------|------------|
| `POST /api/signal/analyze` | `SignalController` |
| `POST /api/risk/evaluate` | `RiskController` |
| `POST /api/trade/execute` | `TradeController` |
| `GET  /api/position` / `/api/position/{pair}` / `POST /api/position/{id}/close` | `PositionController` |
| `GET  /api/account` | `AccountController` |
| `GET  /api/audit` | `AuditController` |
| `POST /api/backtest/run` | `BacktestController` |
| `POST /api/ea/deploy` | `EaController` |
| `*    /api/mifx-bridge/*` | `MifxBridgeController` (EA inbound) |
| `*    /api/mt5-bridge/*` | `Mt5BridgeController` (Exness EA path, secondary) |
| `GET  /api/system/status` | `SystemController` |
| `GET  /api/market/*` | `MarketController` |

SignalR hub: `/hub/dashboard` (live position + signal push ke frontend).

Detail di [docs/api-reference.md](docs/api-reference.md).

## Conventions

- **Domain layer pure** — no IO, no framework refs. Logic murni di-test dari Application layer.
- **JSON repositories** untuk persistence — file-backed di `data/{mode}/`. Atomic write via temp file + rename.
- **camelCase** untuk semua JSON request/response. Enum jadi string (e.g. `"BUY"`, `"ACTIVE"`).
- **Mode-aware storage** — `data/demo/` dan `data/real/` terpisah supaya history simulasi tidak mix dengan real.
- **Runtime data gitignored** — kecuali `mode-state.json` (small, helps reproducibility).
- **EA contract** — semua tick/candle/account payload didefinisikan di [mql5/ForexAI_Bridge.mq5](mql5/ForexAI_Bridge.mq5). Versi EA bump kalau payload berubah.

## Things to NOT do

- Jangan reintroduce dependency ke `_bmad-output/` atau folder BMAD lama (sudah dihapus).
- Jangan bypass `RuleBasedRiskEvaluator` invariant di kode produksi — kalau perlu override untuk test, mock `IRiskEvaluator`.
- Jangan tulis ke `data/demo/*.json` atau `*.jsonl` langsung — semua via repository / service layer (untuk atomic write + audit).
- Jangan commit live MT5 account credential atau MIFX server config.
