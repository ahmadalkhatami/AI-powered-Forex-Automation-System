# Architecture — AI-powered Forex Automation System

## Overview

Semi-automated EUR/USD trading platform untuk timeframe M15/H1. Backend **C# .NET 8 Clean Architecture**, frontend **Next.js**, eksekusi via **MIFX MT5 Expert Advisor bridge**.

Sumber data: tick + candle (M15/H1/D1) di-push real-time dari MT5 EA ke API. Sinyal di-generate sepenuhnya di code path (`LiveSignalAnalyzer`) — tidak ada dependency external pipeline.

Default state: **simulation** (file `data/mode-state.json`, mode auto-detect dari EA `AccountInfoString`).

---

## Architecture Diagram

```
                            ┌─────────────────────┐
                            │  MT5 (MetaTrader 5) │
                            │  + MIFX EA bridge    │
                            └──┬──────────┬───────┘
                               │          │
                  tick/candle  │          │  order/close commands
                  account info ▼          ▲
┌──────────────────────────────────────────────────────────────────┐
│                          ForexAI.API                              │
│   MifxBridgeController (EA inbound) ── MifxCommandQueue (out)     │
│   SignalController, RiskController, TradeController, ...          │
│   DashboardHub (SignalR push ke frontend)                         │
└────────────────────────────┬───────────────────────────────────┘
                             │  injected interfaces
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│                       ForexAI.Application                        │
│   UseCases: AnalyzeSignal, EvaluateRisk, ExecuteTrade,           │
│             ClosePosition, GetAccountHealth,                     │
│             GetPositionStatus, GetAllPositions                   │
└────────┬─────────────────────────────────────────┬───────────────┘
         │  abstractions only                       │
         ▼                                          ▼
┌─────────────────────────┐          ┌─────────────────────────────┐
│     ForexAI.Domain      │          │    ForexAI.Infrastructure   │
│                         │◀─────────│                             │
│  Entities:              │ implements│  Services/                 │
│   TradeSignal           │           │   LiveSignalAnalyzer       │
│   TradePosition         │           │   RuleBasedRiskEvaluator   │
│                         │           │   BacktestRunner           │
│  Value Objects:         │           │   EaDeployService          │
│   MarketSnapshot        │           │   AuditLogger              │
│   CandleBar             │           │  Mifx/                     │
│   ...                   │           │   MifxFullDataService      │
│                         │           │   MifxCandleDataService    │
│  Interfaces:            │           │   MifxBrokerService        │
│   ISignalAnalyzer       │           │   MifxCommandQueue         │
│   IRiskEvaluator        │           │   MifxPositionSyncService  │
│   IMarketDataService    │           │   MifxCandleFeed           │
│   ICandleDataService    │           │   MifxPriceFeed            │
│   IBrokerService        │           │  Persistence/Repositories/ │
│   IModeService          │           │   JsonSignalRepository     │
│   ISystemStateService   │           │   JsonTradePositionRepo    │
│   I*Repository          │           │  Broker/                   │
│                         │           │   NullBrokerService        │
└─────────────────────────┘           └─────────────────────────────┘

Dependency rule: arrows point INWARD.
Domain has zero external dependencies.
Infrastructure depends on Domain. API depends on Application + Infrastructure (DI only).
```

---

## Layer Breakdown

### 1. ForexAI.Domain

Innermost layer. Pure C#, zero IO/framework deps.

**Entities**

| Type | Description |
|---|---|
| `TradeSignal` | Sinyal pasar dengan direction, confidence, embedded analysis. Carry `RiskValidation` setelah lewat risk gate. |
| `TradePosition` | Posisi open/closed: entry, exit, lot, P&L, status. |

**Value Objects (selected)**

| Type | Description |
|---|---|
| `MarketSnapshot` | Snapshot harga, MA20/50 (M15+H1+D1), RSI, S/R zone, session, ATR(14), ADX(14), regime tag. |
| `CandleBar` | OHLCV bar dari EA. |
| `TradeParameters` | Spec trade: direction, lot, entry, SL, TP. |
| `RiskValidation` | Output risk gate: `PositionDecision` (GO / NO_GO / GO_WITH_CAUTION), adjusted lot, rejection reason. |

**Interfaces (di [src/ForexAI.Domain/Interfaces/](../src/ForexAI.Domain/Interfaces/))**

| Interface | Role |
|---|---|
| `ISignalAnalyzer` | Generate `TradeSignal` dari market data. |
| `IRiskEvaluator` | Apply risk rules → `RiskValidation`. |
| `IMarketDataService` | Fetch `MarketSnapshot` (current pair/TF). |
| `ICandleDataService` | Fetch historical candles untuk SMA D1 / regime detection. |
| `IBrokerService` | Send order/close ke broker. |
| `IModeService` | Demo vs Real mode tracking + change event. |
| `ISystemStateService` | Cross-cutting state: peak equity, STOP flag, daily counters. |
| `ITradePositionRepository`, `ISignalRepository` | Persistence contracts. |

---

### 2. ForexAI.Application

Orchestration layer. Reference Domain abstraction saja, never Infrastructure types.

Use cases live di [src/ForexAI.Application/UseCases/](../src/ForexAI.Application/UseCases/) — satu folder per use case (handler + request/response). Saat ini tidak pakai MediatR — handler di-resolve langsung via DI.

| Use Case | Role |
|---|---|
| `AnalyzeSignal` | Call `IMarketDataService` + `ICandleDataService` → invoke `ISignalAnalyzer` → persist via `ISignalRepository`. |
| `EvaluateRisk` | Load signal, invoke `IRiskEvaluator`, persist updated signal dengan `RiskValidation`. |
| `ExecuteTrade` | Construct `TradeParameters`, create `TradePosition`, call `IBrokerService` (atau simulasi), persist. |
| `ClosePosition` | Close position via broker, update `TradePosition` ke `CLOSED_WIN`/`CLOSED_LOSS`. |
| `GetPositionStatus` | Query posisi by pair atau ID. |
| `GetAllPositions` | List semua posisi (filter by status). |
| `GetAccountHealth` | Equity, balance, peak equity, drawdown %. |

---

### 3. ForexAI.Infrastructure

Adapter layer. Implementasi konkret untuk semua interface Domain. Layer ini yang melakukan IO.

**Services**

| File | Role |
|---|---|
| [LiveSignalAnalyzer.cs](../src/ForexAI.Infrastructure/Services/LiveSignalAnalyzer.cs) | `ISignalAnalyzer` aktual. Hitung MA crossover (M15+H1), RSI(14), proximitas ke S/R zone, ATR-based dynamic SL/TP, ADX trend strength, regime classification, HTF D1 alignment veto. Output sinyal + confidence 0–100. |
| [RuleBasedRiskEvaluator.cs](../src/ForexAI.Infrastructure/Services/RuleBasedRiskEvaluator.cs) | `IRiskEvaluator`. Enforce risk invariant (1% per trade, max DD 10%, max 3 posisi, min confidence 60). Output `PositionDecision` + adjusted lot. |
| [BacktestRunner.cs](../src/ForexAI.Infrastructure/Services/BacktestRunner.cs) | Replay `signal-history.json` untuk evaluate strategi changes. |
| [EaDeployService.cs](../src/ForexAI.Infrastructure/Services/EaDeployService.cs) | Copy + compile MQL5 EA ke MT5 terminal folder. |
| [TechnicalIndicators.cs](../src/ForexAI.Infrastructure/Services/TechnicalIndicators.cs) | Pure helper untuk MA/RSI/ATR/ADX (dipakai LiveSignalAnalyzer + BacktestRunner). |

**Mifx bridge** (di [src/ForexAI.Infrastructure/Mifx/](../src/ForexAI.Infrastructure/Mifx/))

| Component | Role |
|---|---|
| `MifxPriceFeed` | Singleton in-memory current tick (last bid/ask + MA/RSI/S-R yang di-push EA). |
| `MifxCandleFeed` | Singleton cache candle M15/H1/D1 dari EA push. |
| `MifxCommandQueue` | Outbound command queue (order/close) yang di-poll EA. |
| `MifxFullDataService` | `IMarketDataService` impl — gabungkan tick + computed indicators jadi `MarketSnapshot`. |
| `MifxCandleDataService` | `ICandleDataService` impl — return cached candle dari `MifxCandleFeed`. |
| `MifxBrokerService` | `IBrokerService` impl — enqueue order ke `MifxCommandQueue`, await EA confirmation. |
| `MifxPositionSyncService` | Listen EA position report, sync ke `JsonTradePositionRepository`. |

**Persistence** (di [src/ForexAI.Infrastructure/Persistence/Repositories/](../src/ForexAI.Infrastructure/Persistence/Repositories/))

`JsonSignalRepository` dan `JsonTradePositionRepository` — file-backed di `data/{mode}/`. Atomic write via temp file + rename.

**Alternative broker adapters** (tidak aktif di default DI — sisa eksplorasi awal)

- `Services/Deriv/` — Deriv WebSocket client
- `Services/Exness/` — Exness Local MT5 + MetaAPI HTTP
- `Broker/NullBrokerService.cs` — no-op fallback ketika `Mifx:EnableExecution = false`

---

### 4. ForexAI.API

HTTP entry point. ASP.NET Core 8, Kestrel di `http://localhost:8080`.

**Controllers** ([src/ForexAI.API/Controllers/](../src/ForexAI.API/Controllers/))

| Controller | Purpose |
|---|---|
| `SignalController` | `POST /api/signal/analyze` — generate sinyal |
| `RiskController` | `POST /api/risk/evaluate` — risk gate |
| `TradeController` | `POST /api/trade/execute` |
| `PositionController` | List/get/close posisi |
| `AccountController` | Account health snapshot |
| `MarketController` | Current market data |
| `AuditController` | Tail audit log |
| `BacktestController` | Jalankan replay backtest |
| `EaController` | Deploy / compile EA |
| `SystemController` | System status, STOP flag |
| `MifxBridgeController` | EA inbound: tick, candle, position report |
| `Mt5BridgeController` | Legacy Exness MT5 bridge endpoint |

**SignalR Hub** ([src/ForexAI.API/Hubs/DashboardHub.cs](../src/ForexAI.API/Hubs/DashboardHub.cs)) — `/hub/dashboard`. Push event real-time ke frontend (live PnL, position updates, signal events).

**Program.cs** registers:
- Controllers + SignalR (CamelCase JSON, string enum)
- CORS (origin: `http://localhost:3000` dan `:3001`, `AllowCredentials`)
- Swagger (dev only)
- Global exception handler (`InvalidOperationException` → 503, lainnya → 500)
- DI via `AddApplication()` + `AddInfrastructure(configuration)`

---

## Configuration

[src/ForexAI.API/appsettings.json](../src/ForexAI.API/appsettings.json):

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http8080": { "Url": "http://localhost:8080" },
      "Http5033": { "Url": "http://127.0.0.1:5033" }
    }
  },
  "Mifx": { "EnableExecution": true }
}
```

`Mifx:EnableExecution = false` → `IBrokerService` resolve ke `NullBrokerService` (simulasi tanpa kirim order ke EA).

---

## Hard Risk Invariants

Enforced di [`RuleBasedRiskEvaluator`](../src/ForexAI.Infrastructure/Services/RuleBasedRiskEvaluator.cs). System-level, tidak bisa di-override via API/config.

| Constraint | Value | Consequence |
|---|---|---|
| Risk per trade | 1% equity | Lot size auto-adjust |
| Min predictor confidence | 60 | Auto `NO_GO` |
| Max open positions | 3 | Auto `NO_GO` sampai posisi close |
| Max drawdown | 10% | System-wide STOP (`SystemStateService.IsHalted = true`) |
| Auto-approve threshold | confidence ≥ 70% | Skip human approval |
| Max trade/hari | 7 | Time-window throttle |

---

## Key Design Decisions

### Clean Architecture

Memungkinkan swap adapter tanpa sentuh business logic. Contoh: pindah dari MIFX EA ke OANDA REST = bikin `OandaBrokerService : IBrokerService`, register di DI, selesai.

### Use cases without MediatR

Dipertimbangkan tapi tidak dipakai. Use case di-resolve langsung dari DI (constructor-injected ke controller atau service yang invoke). Lebih simpel untuk size project saat ini.

### JSON file repository

File-backed persistence cukup untuk simulation single-session. Trade-off: tidak ada concurrent write safety, no transactions — acceptable selama belum scale multi-user. Atomic write pakai temp file + `File.Move(overwrite: true)`.

### Mode-aware storage

`data/demo/` dan `data/real/` terpisah. `ModeService` auto-detect mode dari EA `AccountInfoString` push (REAL → `TradeMode.Real`, lain → `TradeMode.Demo`). Switch mode trigger event → repositories pivot folder target. `mode-state.json` persist di `data/` root (chicken-and-egg: lokasi mode-aware bergantung mode).

### EA push, API pull-aware

EA push tick/candle/position via REST. API tidak pull dari broker — semua state datang dari EA. Command keluar dari API ke EA via `MifxCommandQueue` yang di-poll EA.

---

## Extension Points

| Capability | Approach |
|---|---|
| OANDA / IB live execution | Implement `IBrokerService`, register di DI |
| ML-based signal | Implement `ISignalAnalyzer` (replace `LiveSignalAnalyzer`) |
| Database persistence | Implement `EfCore*Repository`, swap registration |
| Real-time TradingView chart | Sudah ada via SignalR hub + frontend chart component |
| Multi-pair | Loop di scheduler service, pass pair ke `IMarketDataService.GetSnapshotAsync(pair, tf)` |
| Audit pipeline behavior | `AuditLogger` sudah ada — extend untuk cover use case lain |
