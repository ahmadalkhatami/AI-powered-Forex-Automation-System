# Architecture — AI-powered Forex Automation System

## Overview

The AI-powered Forex Automation System is a semi-automated, simulation-first trading platform targeting EUR/USD on M15/H1 timeframes. The backend is implemented in **C# .NET 8** following **Clean Architecture** principles. It coordinates a multi-stage pipeline: market signal analysis → AI-backed prediction validation → risk management gate → trade execution (simulation or live).

The system is deliberately in **simulation phase** during initial development. All infrastructure adapters either read from pre-generated BMAD planning artifact JSON files or persist state to local JSON files, enabling full end-to-end testing without a live broker connection.

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        ForexAI.API                                  │
│   SignalController  RiskController  TradeController  PositionCtrl   │
│   Program.cs  (CORS, Swagger dev-only, Global Exception Handler)    │
└────────────────────────────┬────────────────────────────────────────┘
                             │  HTTP requests → MediatR commands/queries
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     ForexAI.Application                             │
│   Commands / Queries (CQRS via MediatR):                            │
│     AnalyzeSignalCommand   EvaluateRiskCommand                      │
│     ExecuteTradeCommand    GetPositionStatusQuery                   │
│   Handlers orchestrate domain logic via interfaces                  │
└──────────┬──────────────────────────────────────────┬──────────────┘
           │  depends on (abstractions only)           │
           ▼                                           ▼
┌─────────────────────────┐           ┌───────────────────────────────┐
│     ForexAI.Domain      │           │     ForexAI.Infrastructure    │
│                         │◄──────────│                               │
│  Entities               │ implements│  JsonSignalRepository         │
│  Value Objects          │           │  JsonTradePositionRepository  │
│  Interfaces             │           │  StubMarketDataService        │
│  Enums                  │           │  BmadSignalAnalyzer           │
│                         │           │  RuleBasedRiskEvaluator       │
└─────────────────────────┘           └───────────────────────────────┘

Dependency rule: arrows point INWARD.
Domain has zero external dependencies.
Infrastructure depends on Domain; API depends on Application + Infrastructure (for DI wiring only).
```

---

## Layer Breakdown

### 1. ForexAI.Domain

The innermost layer. Contains all business concepts with no framework or I/O dependencies — pure C#.

**Responsibilities**
- Define the core vocabulary of the trading domain.
- Express business invariants and rules as first-class types.
- Declare contracts (interfaces) that outer layers must satisfy.

**Entities**

| Type | Description |
|---|---|
| `TradeSignal` | Represents a market signal with direction, confidence score, and embedded analysis results. Owns a `PredictorResult` and `RiskValidation` once the pipeline stages complete. |
| `TradePosition` | Represents an open or closed trade position. Carries entry/exit prices, lot size, P&L, and lifecycle status via `TradeStatus`. |

**Value Objects**

| Type | Description |
|---|---|
| `MarketSnapshot` | Immutable snapshot of current price, spread, and session data used as pipeline input. |
| `TrendAnalysis` | Encapsulates Moving Average crossover results (MA20/MA50/MA200 alignment, trend direction). |
| `MomentumAnalysis` | Encapsulates RSI reading, overbought/oversold state, and momentum direction. |
| `StructureAnalysis` | Encapsulates Support/Resistance levels, proximity to key levels, and structural bias. |
| `TradeParameters` | Immutable specification for a trade: direction, lot size, entry price, stop-loss, take-profit. |
| `PredictorResult` | Output from the AI predictor stage: confidence score (0–100) and validation narrative. |
| `RiskValidation` | Output from the risk gate: `PositionDecision` (GO / NO_GO / GO_WITH_CAUTION), adjusted lot size, rejection reason if applicable. |

**Interfaces**

| Interface | Role |
|---|---|
| `ISignalAnalyzer` | Contract for analyzing market data and producing a `TradeSignal`. |
| `IRiskEvaluator` | Contract for evaluating a signal against risk rules and returning a `RiskValidation`. |
| `IMarketDataService` | Contract for retrieving the current `MarketSnapshot`. |
| `ISignalRepository` | Contract for persisting and retrieving `TradeSignal` records. |
| `ITradePositionRepository` | Contract for persisting and retrieving `TradePosition` records. |

**Enums**

| Enum | Values |
|---|---|
| `SignalDirection` | `BUY`, `SELL`, `HOLD` |
| `TradeStatus` | `Pending`, `Open`, `Closed`, `Cancelled` |
| `PositionDecision` | `GO`, `NO_GO`, `GO_WITH_CAUTION` |

---

### 2. ForexAI.Application

The orchestration layer. References only Domain abstractions — never infrastructure types directly.

**Responsibilities**
- Define CQRS commands and queries.
- Implement MediatR handlers that wire together domain interfaces.
- Enforce pipeline sequencing: signal → predictor validation → risk gate → execution.
- Return structured result DTOs to the API layer.

**Use Cases (Commands & Queries)**

| Name | Type | Description |
|---|---|---|
| `AnalyzeSignalCommand` | Command | Requests a full market analysis. The handler calls `IMarketDataService` to fetch a snapshot, then `ISignalAnalyzer` to produce a `TradeSignal`, then persists via `ISignalRepository`. Returns the signal DTO including embedded `TrendAnalysis`, `MomentumAnalysis`, and `StructureAnalysis`. |
| `EvaluateRiskCommand` | Command | Takes a `TradeSignal` ID, loads it via `ISignalRepository`, calls `IRiskEvaluator`, attaches the resulting `RiskValidation` to the signal, and re-persists. Returns the `PositionDecision` and lot-sizing output. |
| `ExecuteTradeCommand` | Command | Takes a validated `TradeSignal` (must have a GO or GO_WITH_CAUTION decision). Constructs `TradeParameters`, creates a `TradePosition` entity, and persists via `ITradePositionRepository`. In simulation phase this is a dry-run; no broker call occurs. |
| `GetPositionStatusQuery` | Query | Returns a snapshot of all active `TradePosition` records from `ITradePositionRepository`. Supports filtering by `TradeStatus`. |

**Handler Pattern**

Each handler follows the same structure:
1. Validate command/query inputs.
2. Load domain state from repository interfaces.
3. Invoke domain logic (pure) or delegate to service interfaces.
4. Persist updated state.
5. Map domain objects to response DTOs and return.

No handler contains business logic — all rules live in Domain or in the implementing infrastructure adapters.

---

### 3. ForexAI.Infrastructure

The adapter layer. Provides concrete implementations of every interface declared in Domain. This is the only layer that performs I/O (file reads, JSON parsing).

**Responsibilities**
- Implement Domain interfaces with real (or stub) I/O adapters.
- Own all serialization/deserialization concerns (System.Text.Json).
- Integrate with external data sources (BMAD planning artifacts, future broker APIs).
- Remain swappable: any adapter can be replaced without touching Domain or Application.

**Implementations**

#### `JsonSignalRepository`
Implements `ISignalRepository`. Persists `TradeSignal` entities as JSON to `_bmad-output/planning-artifacts/signal-output.json`. Uses append-or-overwrite semantics suitable for single-session simulation runs.

#### `JsonTradePositionRepository`
Implements `ITradePositionRepository`. Persists `TradePosition` entities to `_bmad-output/implementation-artifacts/execution-log.json`. Reads the full array on every query and writes it back atomically on every mutation. Acceptable at simulation scale.

#### `StubMarketDataService`
Implements `IMarketDataService`. Rather than calling a live broker, reads the current `MarketSnapshot` from `_bmad-output/planning-artifacts/signal-output.json` — the artifact produced by the BMAD forex pipeline. Allows end-to-end testing with realistic market data without any API credentials.

#### `BmadSignalAnalyzer`
Implements `ISignalAnalyzer`. Reads the pre-computed analysis from two BMAD artifacts:
- `signal-output.json` — provides `TrendAnalysis`, `MomentumAnalysis`, `StructureAnalysis`, and initial `SignalDirection`.
- `risk-decision.json` — provides the predictor confidence and `PositionDecision` context.

Hydrates and returns a fully-formed `TradeSignal` entity, effectively bridging the BMAD agent pipeline output into the .NET domain model.

#### `RuleBasedRiskEvaluator`
Implements `IRiskEvaluator`. Pure logic adapter — no I/O. Applies the system's hard risk invariants:

| Rule | Value |
|---|---|
| Risk per trade | 1% of account equity |
| Minimum predictor confidence for GO | 60 |
| Maximum open positions | 3 |
| Maximum drawdown before system STOP | 10% |

Returns a `RiskValidation` value object with the computed `PositionDecision`, adjusted lot size (Kelly-fraction capped at 1% risk), and a rejection reason string when NO_GO is issued.

---

### 4. ForexAI.API

The entry point layer. Hosts the HTTP API, configures the DI container, and translates HTTP contracts to MediatR dispatches.

**Responsibilities**
- Register all services, repositories, and handlers in `Program.cs`.
- Route HTTP requests to the appropriate MediatR command or query.
- Apply cross-cutting concerns: CORS, Swagger (dev only), global exception handling.
- Return consistent HTTP response shapes.

**Controllers**

| Controller | Endpoints | Dispatches |
|---|---|---|
| `SignalController` | `POST /api/signals/analyze` | `AnalyzeSignalCommand` |
| `RiskController` | `POST /api/risk/evaluate` | `EvaluateRiskCommand` |
| `TradeController` | `POST /api/trades/execute` | `ExecuteTradeCommand` |
| `PositionController` | `GET /api/positions` | `GetPositionStatusQuery` |

**Program.cs Configuration**

```
Services registered:
  - MediatR (scans ForexAI.Application assembly)
  - ISignalAnalyzer        → BmadSignalAnalyzer
  - IRiskEvaluator         → RuleBasedRiskEvaluator
  - IMarketDataService     → StubMarketDataService
  - ISignalRepository      → JsonSignalRepository
  - ITradePositionRepository → JsonTradePositionRepository

Middleware pipeline (in order):
  1. Global Exception Handler (maps domain exceptions to RFC 7807 ProblemDetails)
  2. CORS (permissive in development, restricted by origin in production)
  3. Swagger / SwaggerUI — only registered when ASPNETCORE_ENVIRONMENT == Development
  4. Authentication / Authorization (placeholder, ready for JWT expansion)
  5. Controllers
```

---

## Dependency Flow

```
HTTP Request
     │
     ▼
ForexAI.API (Controller)
     │
     │  IMediator.Send(command)
     ▼
ForexAI.Application (Handler)
     │
     │  calls interfaces: ISignalAnalyzer, IRiskEvaluator,
     │  IMarketDataService, ISignalRepository, ITradePositionRepository
     ▼
ForexAI.Domain (Entities, Value Objects, Interfaces)
     ▲
     │  implements interfaces
ForexAI.Infrastructure (Adapters)
     │
     │  reads/writes
     ▼
File System (_bmad-output/*.json)
```

**Inward-only rule**: every `using` / project reference points toward Domain. Domain imports nothing outside the BCL.

---

## Key Design Decisions

### Why Clean Architecture?

**Problem**: A trading system must support rapid iteration on business logic (signal strategies, risk rules) while also swapping infrastructure (simulation → live broker, JSON → database, stub analyzer → real ML model). Tightly coupled architectures make either change expensive.

**Decision**: Clean Architecture enforces a hard boundary between what the system *does* (Domain + Application) and how it *does it* (Infrastructure). This means:
- The `RuleBasedRiskEvaluator` can be replaced with an ML-based evaluator without touching a single command handler.
- The `StubMarketDataService` can be swapped for an OANDA REST adapter by registering a different binding in `Program.cs`.
- Domain logic can be unit-tested with zero mocking frameworks — just inject in-memory implementations of the interfaces.

### Why CQRS with MediatR?

**Problem**: As the pipeline grows (more signal types, additional risk checks, audit logging, notification hooks), a traditional service-layer approach causes handler classes to accumulate unrelated concerns and become difficult to test in isolation.

**Decision**: CQRS via MediatR gives each use case a single, focused handler class. Benefits:

- **Explicit intent**: `AnalyzeSignalCommand` vs `GetPositionStatusQuery` makes reads and writes structurally distinct.
- **Extensibility via behaviors**: Cross-cutting concerns (logging, validation, performance timing) attach as `IPipelineBehavior<,>` decorators without modifying handlers.
- **Testability**: Each handler is a plain class with constructor-injected interfaces. Unit tests instantiate the handler directly.
- **Future event sourcing readiness**: Commands map naturally to domain events if an event store is introduced later.

### Why JSON File Persistence for the Simulation Phase?

**Problem**: During simulation, the system must bridge output from the BMAD agent pipeline (which produces JSON artifacts) into the .NET domain model. Introducing a database at this stage adds operational complexity without delivering value.

**Decision**: JSON file persistence via `System.Text.Json` is sufficient because:

- **Zero infrastructure cost**: No database server, migration scripts, or connection strings needed to run the system.
- **BMAD artifact integration**: `StubMarketDataService` and `BmadSignalAnalyzer` read directly from `_bmad-output/planning-artifacts/`, treating BMAD agent outputs as the data source. This makes the .NET backend a consumer of the existing AI pipeline rather than duplicating it.
- **Human-readable audit trail**: All signals and positions are inspectable without tooling — useful during early validation of trading logic.
- **Replaceable at graduation**: Both repository implementations are behind `ISignalRepository` and `ITradePositionRepository`. Migrating to PostgreSQL, SQLite, or Cosmos DB is a matter of writing two new classes and changing two DI registrations.

The tradeoff (no transactions, no concurrent write safety) is explicitly acceptable because simulation runs are single-session and single-user.

---

## Hard Risk Invariants

These constraints are enforced by `RuleBasedRiskEvaluator` and are system-level invariants — no configuration or command can override them at runtime:

| Constraint | Value | Consequence of breach |
|---|---|---|
| Risk per trade | 1% of equity | Lot size is adjusted down automatically |
| Minimum predictor confidence | 60 / 100 | Automatic `NO_GO`, trade is not executed |
| Maximum open positions | 3 | Automatic `NO_GO` until a position closes |
| Maximum drawdown | 10% of account | System-wide STOP; `ExecuteTradeCommand` rejects all new trades |

---

## Future Extension Points

| Capability | Extension Approach |
|---|---|
| Live OANDA execution | Implement `IOandaTradeExecutor`, inject via DI, guard with feature flag |
| MT5 bridge (Windows VM) | New Infrastructure adapter behind `ITradeExecutionBridge` |
| Real-time ML predictor | Replace `BmadSignalAnalyzer` with `MlPredictorSignalAnalyzer` implementing `ISignalAnalyzer` |
| Persistent database | Implement `EfCoreSignalRepository` and `EfCoreTradePositionRepository`; register in Program.cs |
| WebSocket position feed | Add SignalR hub that publishes `GetPositionStatusQuery` results on a timer |
| Audit / event log | Add `AuditLoggingBehavior : IPipelineBehavior<,>` — zero handler changes required |
