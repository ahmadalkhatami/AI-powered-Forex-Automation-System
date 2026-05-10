# Story 1.2: Market Data Stub and Signal Analyzer

Status: review

## Story

As a backend developer,
I want stub implementations of `IMarketDataService` and `ISignalAnalyzer` that return fixture data from the existing JSON artifacts,
so that the AnalyzeSignal use case can run end-to-end in simulation without a live data feed.

## Acceptance Criteria

1. `StubMarketDataService` in `src/ForexAI.Infrastructure/Services/` implements `IMarketDataService`:
   - `GetSnapshotAsync(pair, timeframe)` reads `_bmad-output/planning-artifacts/signal-output.json` and returns a `MarketSnapshot`
   - Returns the same fixture regardless of pair/timeframe (simulation mode only)
2. `BmadSignalAnalyzer` in `src/ForexAI.Infrastructure/Services/` implements `ISignalAnalyzer`:
   - `AnalyzeAsync(MarketSnapshot snapshot)` reads `signal-output.json` and maps it to a `TradeSignal` entity
   - Maps `analysis.trend` → `TrendAnalysis` value object (all fields)
   - Maps `analysis.momentum` → `MomentumAnalysis` value object (all fields)
   - Maps `analysis.structure` → `StructureAnalysis` value object (all fields)
   - Maps `score_breakdown` fields → `TradeParameters` from `risk-decision.json` trade_parameters section
   - Returns a fully populated `TradeSignal` with all value objects correctly set
3. `DependencyInjection.cs` updated to register both services as scoped
4. `dotnet build src/ForexAI.Infrastructure/ForexAI.Infrastructure.csproj` passes with zero errors

## Tasks / Subtasks

- [x] Create `StubMarketDataService` (AC: 1)
  - [x] Read `signal-output.json` using `System.Text.Json`
  - [x] Map top-level fields to `MarketSnapshot` record
- [x] Create `BmadSignalAnalyzer` (AC: 2)
  - [x] Deserialize `signal-output.json` into an anonymous type / local DTO
  - [x] Map `analysis.trend` → `TrendAnalysis`
  - [x] Map `analysis.momentum` → `MomentumAnalysis`
  - [x] Map `analysis.structure` → `StructureAnalysis`
  - [x] Read `risk-decision.json` for trade parameters (entry, SL, TP, lot, risk)
  - [x] Build `TradeParameters` from risk-decision trade_parameters section
  - [x] Construct and return `TradeSignal` using public constructor
- [x] Update `DependencyInjection.cs` to register both services (AC: 3)
- [x] Verify build (AC: 4)

## Dev Notes

### signal-output.json → MarketSnapshot Mapping

```
signal-output.json field          → MarketSnapshot property
─────────────────────────────────────────────────────────
pair                              → Pair
timeframe                         → Timeframe
(from analysis.trend.configuration) parse: MA20 M15, MA50 M15, MA20 H1, MA50 H1
analysis.momentum.rsi_value       → RSI14
analysis.momentum.direction       → RSIDirection
(from risk-decision.json)         → SupportZone, ResistanceZone
"London Open"                     → Session (hardcoded for stub)
timestamp                         → CapturedAt
```

`MarketSnapshot` fields that aren't directly in signal-output: parse from `analysis.trend.configuration` string:
- `"MA20: 1.1252, MA50: 1.1238, gap: 14 pip, slope: 3 pip/candle, MA20-MA50 gap established ~6 jam"`
- Parse regex or split to extract MA20, MA50 values

### signal-output.json → TrendAnalysis Mapping

```
analysis.trend.bias               → Bias
analysis.trend.strength           → Strength
analysis.trend.score              → Score
analysis.trend.htf_aligned        → HtfAligned
analysis.trend.configuration      → Configuration
analysis.trend.score_rationale    → ScoreRationale
```

### signal-output.json → MomentumAnalysis Mapping

```
analysis.momentum.rsi_value       → RSIValue
analysis.momentum.direction       → RSIDirection
analysis.momentum.zone            → Zone
analysis.momentum.score           → Score
analysis.momentum.score_rationale → ScoreRationale
(no divergence in current data)   → Divergence = null
```

### signal-output.json → StructureAnalysis Mapping

```
analysis.structure.nearest_support    → NearestSupport
analysis.structure.nearest_resistance → NearestResistance
analysis.structure.score              → Score
analysis.structure.score_rationale    → ScoreRationale
analysis.structure.candle_confirmed   → CandleConfirmed
analysis.structure.candle_pattern     → CandlePattern
(derive from price vs support/resistance) → PricePosition
```

### risk-decision.json → TradeParameters Mapping

```
risk-decision.json trade_parameters field  → TradeParameters property
──────────────────────────────────────────────────────────────────────
entry                                      → Entry
stop_loss                                  → StopLoss
stop_loss_pips                             → StopLossPips
take_profit                                → TakeProfit
take_profit_pips                           → TakeProfitPips
lot_size                                   → LotSize
risk_amount_usd                            → RiskAmount
potential_profit_usd                       → PotentialProfit
risk_reward_ratio                          → RiskRewardRatio
```

### TradeSignal Constructor Parameters

The `TradeSignal` public constructor requires:
```csharp
new TradeSignal(
    runId: "RUN-20260505-002",           // from signal-output.json run_id (or generate new)
    pair: "EUR/USD",
    timeframe: "M15",
    signal: SignalDirection.BUY,          // parse signal string → enum
    confluenceScore: 3,                   // confluence_score
    confidenceScore: 0.66m,              // confidence_score
    snapshot: marketSnapshot,
    trend: trendAnalysis,
    momentum: momentumAnalysis,
    structure: structureAnalysis,
    parameters: tradeParameters,
    warnings: new List<string> { "..." } // from risk-decision.json signal_quality.active_warnings
)
```

### PredictorResult in BmadSignalAnalyzer

`BmadSignalAnalyzer` should also read `predictor-decision.json` and provide a `PredictorResult` — but the current `ISignalAnalyzer` interface only returns `TradeSignal`, not `PredictorResult`. 

**Decision**: `PredictorResult` is separate from `TradeSignal`. The `EvaluateRiskCommand` takes `PredictorResult` as a separate parameter. For now, the `EvaluateRiskHandler` (Story 1.3) will read `predictor-decision.json` directly via a stub.

This is acceptable for simulation — in a real system, the predictor result would come from an actual ML model call.

### File Path Handling

Same pattern as Story 1.1 — resolve relative to `Directory.GetCurrentDirectory()`.

### Project Structure Notes

New files to create:

```
src/ForexAI.Infrastructure/
└── Services/
    ├── StubMarketDataService.cs    NEW
    └── BmadSignalAnalyzer.cs       NEW
```

Update:
```
src/ForexAI.Infrastructure/DependencyInjection.cs   UPDATE — add 2 new registrations
```

### References

- [Source: src/ForexAI.Domain/Interfaces/IMarketDataService.cs]
- [Source: src/ForexAI.Domain/Interfaces/ISignalAnalyzer.cs]
- [Source: src/ForexAI.Domain/ValueObjects/MarketSnapshot.cs]
- [Source: src/ForexAI.Domain/ValueObjects/TrendAnalysis.cs]
- [Source: src/ForexAI.Domain/ValueObjects/MomentumAnalysis.cs]
- [Source: src/ForexAI.Domain/ValueObjects/StructureAnalysis.cs]
- [Source: src/ForexAI.Domain/ValueObjects/TradeParameters.cs]
- [Source: src/ForexAI.Domain/Entities/TradeSignal.cs]
- [Source: _bmad-output/planning-artifacts/signal-output.json]
- [Source: _bmad-output/planning-artifacts/risk-decision.json]
- [Source: _bmad-output/planning-artifacts/predictor-decision.json]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- ✅ `StubMarketDataService`: membaca `price_snapshot` section dari signal-output.json — field sudah ada semua, tidak perlu parse dari `analysis.trend.configuration` string
- ✅ `BmadSignalAnalyzer`: mapping `analysis.structure.candle_confirmation` (bukan `candle_confirmed`) sesuai JSON aktual
- ✅ `TradeParameters` diambil dari `risk-decision.json trade_parameters` — field `risk_amount` dan `potential_profit` (bukan `_usd` suffix yang ada di story notes)
- ✅ `warnings` dari signal-output.json top-level `warnings` array (3 warning strings)
- ✅ DependencyInjection.cs diupdate dengan 2 registrasi baru: `IMarketDataService` + `ISignalAnalyzer`
- ✅ Build: 0 errors, 0 warnings

### File List

- `src/ForexAI.Infrastructure/Services/StubMarketDataService.cs` — NEW
- `src/ForexAI.Infrastructure/Services/BmadSignalAnalyzer.cs` — NEW
- `src/ForexAI.Infrastructure/DependencyInjection.cs` — MODIFIED (tambah 2 registrasi)
