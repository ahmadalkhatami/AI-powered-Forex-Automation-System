# Story 1.1: JSON File-Based Repositories

Status: review

## Story

As a backend developer,
I want working implementations of `ITradePositionRepository` and `ISignalRepository` that persist to JSON files,
so that the Application layer use cases can store and retrieve trade data without a database.

## Acceptance Criteria

1. `JsonTradePositionRepository` in `src/ForexAI.Infrastructure/Repositories/` implements `ITradePositionRepository`:
   - Reads/writes to `_bmad-output/implementation-artifacts/execution-log.json`
   - `GetActiveByPairAsync(string pair)` returns the active TradePosition for given pair or null
   - `GetOpenPositionsAsync()` returns all positions with Status = ACTIVE
   - `SaveAsync(TradePosition)` upserts by TradeId (update if exists, append if new)
   - `CountOpenPositionsAsync()` returns count of ACTIVE positions
2. `JsonSignalRepository` in `src/ForexAI.Infrastructure/Repositories/` implements `ISignalRepository`:
   - Reads/writes to `_bmad-output/implementation-artifacts/signal-history.json`
   - `GetLatestAsync(string pair)` returns most recent signal for that pair or null
   - `GetByIdAsync(Guid id)` returns signal by Id or null
   - `SaveAsync(TradeSignal)` appends signal to JSON array
3. Both repositories handle missing file gracefully: return null/empty on read, create file on first write
4. `dotnet build src/ForexAI.Infrastructure/ForexAI.Infrastructure.csproj` passes with zero errors and zero warnings

## Tasks / Subtasks

- [x] Create DTO records for serialization (AC: 1, 2)
  - [x] `TradePositionDto.cs` in `src/ForexAI.Infrastructure/Persistence/Dtos/`
  - [x] `TradeSignalDto.cs` in `src/ForexAI.Infrastructure/Persistence/Dtos/`
  - [x] `DtoMapper.cs` with static methods: `ToDto(TradePosition)`, `ToDomain(TradePositionDto)`, `ToDto(TradeSignal)`, `ToDomain(TradeSignalDto)`
- [x] Implement `JsonTradePositionRepository` (AC: 1)
  - [x] Constructor accepts file path string (injected or resolved from `Directory.GetCurrentDirectory()`)
  - [x] `LoadAllAsync()` private method: deserialize `execution_log` array from JSON file
  - [x] `SaveAllAsync(List<TradePositionDto>)` private method: serialize back to file
  - [x] Implement all 4 interface methods using load/save pattern
- [x] Implement `JsonSignalRepository` (AC: 2)
  - [x] Same load/save pattern against `signal_history` JSON array
  - [x] Implement all 3 interface methods
- [x] Add `IServiceCollection AddInfrastructure()` extension method in `DependencyInjection.cs` (AC: 4)
  - [x] Register both repositories as scoped
- [x] Verify build passes (AC: 4)

## Dev Notes

### CRITICAL: Serialization Design Decision

`TradePosition` and `TradeSignal` use **private setters** and **static factory methods** — they cannot be directly deserialized by `System.Text.Json`. The domain entities must NOT be modified to add JSON attributes (that would violate Clean Architecture — domain layer must not depend on infrastructure concerns).

**Solution: DTO layer inside Infrastructure** (NOT in Domain or Application)

Create separate DTO records that are plain data containers, then map between DTOs and domain entities:

```
src/ForexAI.Infrastructure/Persistence/
├── Dtos/
│   ├── TradePositionDto.cs   ← record with all public properties
│   ├── TradeSignalDto.cs     ← record with all public properties
│   └── DtoMapper.cs          ← static mapping methods
└── Repositories/
    ├── JsonTradePositionRepository.cs
    └── JsonSignalRepository.cs
```

### DTO → Domain Mapping

`TradePositionDto` → `TradePosition`:
- When `Status == "ACTIVE"` or `Status == "SIMULATION"`: use `TradePosition.CreateSimulated(...)` factory
- When `Status == "SKIPPED"`: use `TradePosition.CreateSkipped(...)` factory
- When `Status == "CLOSED_WIN"` or `Status == "CLOSED_LOSS"`: use `CreateSimulated(...)` then call `UpdateFloatingPnl` with exit price to trigger auto-close

⚠️ There is no `CreateClosed()` factory. Closed positions are handled by `UpdateFloatingPnl()` auto-closing when price hits SL/TP. For persisted closed positions, consider adding a `CreateFromHistory()` static factory — or use reflection-free reconstruction via `CreateSimulated` + stored final state. Recommended: add `CreateFromHistory()` to `TradePosition` entity in Domain layer.

### Existing execution-log.json Format

The current file uses a different schema than the domain model. This is the BMAD agent format (snake_case, nested objects). The repository must handle BOTH reading this legacy format AND writing the new normalized format.

**Legacy fields in execution-log.json:**
```json
{
  "execution_log": [
    {
      "trade_id": "SIM-20260505-001",
      "status": "SKIPPED",
      "pair": "EUR/USD",
      "direction": null,
      "entry_price": 1.1268,
      "stop_loss": 1.1244,
      "take_profit": 1.1313,
      "lot_size": 0.41,
      "risk_amount": 98.40,
      "skip_reason": "...",
      "run_id": "RUN-20260505-001"
    }
  ]
}
```

**New normalized format (what repositories write):**
```json
{
  "positions": [
    {
      "tradeId": "SIM-20260505-001",
      "runId": "RUN-20260505-001",
      "status": "SKIPPED",
      "pair": "EUR/USD",
      "direction": "HOLD",
      "entry": 0.0,
      "stopLoss": 0.0,
      "takeProfit": 0.0,
      "lotSize": 0.0,
      "riskAmount": 0.0,
      "potentialProfit": 0.0,
      "riskReward": 0.0,
      "floatingPnl": 0.0,
      "floatingPnlPips": 0,
      "openedAt": null,
      "closedAt": null,
      "mode": "SIMULATION",
      "skipReason": "..."
    }
  ]
}
```

**Migration strategy**: On first load, if file contains `"execution_log"` key, parse legacy format and migrate to new format. If file contains `"positions"` key, use new format.

### JsonSerializerOptions

```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
};
```

The `JsonStringEnumConverter` is required so `SignalDirection.BUY` serializes as `"BUY"` not `0`.

### File Path Resolution

```csharp
// Resolve relative to the solution root, not the executing assembly
private static string ResolveFilePath(string relativePath)
{
    var current = Directory.GetCurrentDirectory();
    return Path.GetFullPath(Path.Combine(current, relativePath));
}
```

File paths:
- `execution-log.json` → `_bmad-output/implementation-artifacts/execution-log.json`
- `signal-history.json` → `_bmad-output/implementation-artifacts/signal-history.json`

### TradeSignal Serialization Challenge

`TradeSignal` constructor is public but takes complex value object parameters. `TradeSignalDto` should have flat properties matching all value object fields (e.g., `TrendBias`, `TrendScore`, `TrendHtfAligned` etc.) and `DtoMapper` reconstructs the full graph.

### Infrastructure csproj — No Changes Needed

The existing `ForexAI.Infrastructure.csproj` already has:
- `ProjectReference` to `ForexAI.Application` (which pulls in `ForexAI.Domain`)
- `Microsoft.Extensions.DependencyInjection.Abstractions` for DI registration

No new NuGet packages needed — `System.Text.Json` is included in .NET 8 SDK.

### DependencyInjection.cs Pattern

Follow the same pattern as `ForexAI.Application/DependencyInjection.cs`:

```csharp
namespace ForexAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ITradePositionRepository, JsonTradePositionRepository>();
        services.AddScoped<ISignalRepository, JsonSignalRepository>();
        return services;
    }
}
```

### Project Structure Notes

New files to create (all NEW — nothing being modified):

```
src/ForexAI.Infrastructure/
├── DependencyInjection.cs                          NEW
├── Persistence/
│   ├── Dtos/
│   │   ├── TradePositionDto.cs                     NEW
│   │   ├── TradeSignalDto.cs                       NEW
│   │   └── DtoMapper.cs                            NEW
│   └── Repositories/
│       ├── JsonTradePositionRepository.cs          NEW
│       └── JsonSignalRepository.cs                 NEW
```

Domain file to ADD one factory method (minimal change):

```
src/ForexAI.Domain/Entities/TradePosition.cs        UPDATE — add CreateFromHistory() factory
```

### References

- [Source: src/ForexAI.Domain/Interfaces/ITradePositionRepository.cs]
- [Source: src/ForexAI.Domain/Interfaces/ISignalRepository.cs]
- [Source: src/ForexAI.Domain/Entities/TradePosition.cs]
- [Source: src/ForexAI.Domain/Entities/TradeSignal.cs]
- [Source: src/ForexAI.Domain/ValueObjects/TradeParameters.cs]
- [Source: src/ForexAI.Application/DependencyInjection.cs] — DI pattern to follow
- [Source: _bmad-output/implementation-artifacts/execution-log.json] — legacy format reference

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- ✅ Added `CreateFromHistory()` factory to `TradePosition` — restores closed positions with preserved status, PnL, and timestamps from persistence
- ✅ Added `CreateFromHistory()` factory to `TradeSignal` — required so `GetByIdAsync` can retrieve signals by original Guid after deserialization
- ✅ `TradePositionDto` + `TradeSignalDto`: internal records with all fields as `init`-only properties — not visible outside Infrastructure
- ✅ `DtoMapper`: bidirectional mapping; uses `CreateSkipped` for SKIPPED, `CreateFromHistory` for ACTIVE/CLOSED states
- ✅ `JsonTradePositionRepository`: handles both legacy `execution_log` format (snake_case) and new `positions` format (camelCase) via `ParseLegacyFormat()`
- ✅ `JsonSignalRepository`: appends-on-save pattern; `GetLatestAsync` uses `MaxBy(Timestamp)`; `GetByIdAsync` matches exact GUID
- ✅ `AddInfrastructure()` DI extension: registers both repos as scoped — no file path needed at DI registration because default path resolved from `Directory.GetCurrentDirectory()`
- ✅ Build: `dotnet build ForexAI.Infrastructure.csproj` → 0 errors, 0 warnings

### File List

- `src/ForexAI.Domain/Entities/TradePosition.cs` — MODIFIED (added `CreateFromHistory()` factory)
- `src/ForexAI.Domain/Entities/TradeSignal.cs` — MODIFIED (added `CreateFromHistory()` factory)
- `src/ForexAI.Infrastructure/Persistence/Dtos/TradePositionDto.cs` — NEW
- `src/ForexAI.Infrastructure/Persistence/Dtos/TradeSignalDto.cs` — NEW
- `src/ForexAI.Infrastructure/Persistence/Dtos/DtoMapper.cs` — NEW
- `src/ForexAI.Infrastructure/Persistence/Repositories/JsonTradePositionRepository.cs` — NEW
- `src/ForexAI.Infrastructure/Persistence/Repositories/JsonSignalRepository.cs` — NEW
- `src/ForexAI.Infrastructure/DependencyInjection.cs` — NEW
