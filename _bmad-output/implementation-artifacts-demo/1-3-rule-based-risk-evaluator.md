# Story 1.3: Rule-Based Risk Evaluator

Status: review

## Story

As a backend developer,
I want a rule-based implementation of `IRiskEvaluator` that enforces the hard risk limits defined in CLAUDE.md,
so that the EvaluateRisk use case applies the same gate logic as Reza (the risk management agent).

## Acceptance Criteria

1. `RuleBasedRiskEvaluator` in `src/ForexAI.Infrastructure/Services/` implements `IRiskEvaluator`
2. `EvaluateAsync(signal, predictor, equity, openPositions)` applies gates in this order:
   - NO-GO if `signal.Signal == SignalDirection.HOLD`
   - NO-GO if `predictor.AdjustedConfidence < 0.60m`
   - NO-GO if `openPositions >= 3`
   - GO_WITH_CAUTION if all above pass AND `predictor.AdjustedConfidence < 0.70m`
   - GO if all above pass AND `predictor.AdjustedConfidence >= 0.70m`
3. When GO or GO_WITH_CAUTION: `RiskValidation.ValidatedParameters` is populated from `signal.Parameters`
4. When GO_WITH_CAUTION: `CautionNotes` contains at least one note explaining the caution
5. When NO-GO: `NoGoReasons` list contains at least one human-readable reason string per failed gate
6. `RiskValidation.PositionDecision` set to `PositionDecision.OPEN` when GO/GO_WITH_CAUTION, `PositionDecision.SKIP` when NO-GO
7. `DependencyInjection.cs` updated to register `RuleBasedRiskEvaluator` as `IRiskEvaluator` scoped
8. `dotnet build` passes with zero errors

## Tasks / Subtasks

- [x] Create `RuleBasedRiskEvaluator.cs` (AC: 1–6)
  - [x] Implement HOLD signal gate
  - [x] Implement confidence < 0.60 gate
  - [x] Implement max open positions gate
  - [x] Implement GO_WITH_CAUTION branch (0.60–0.70)
  - [x] Implement full GO branch (>= 0.70)
  - [x] Populate `ValidatedParameters` from `signal.Parameters` when GO/GO_WITH_CAUTION
  - [x] Populate `CautionNotes` when GO_WITH_CAUTION
  - [x] Populate `NoGoReasons` when NO-GO
  - [x] Set `PositionDecision` correctly for each branch
- [x] Update `DependencyInjection.cs` (AC: 7)
- [x] Verify build (AC: 8)

## Dev Notes

### Hard Limits from CLAUDE.md (System Invariants)

These values are non-negotiable and defined in CLAUDE.md:
```
Risk per trade: 1% of equity
Max drawdown: 10% → automatic system STOP
Max open positions: 3
Minimum AI Predictor confidence: 60 → auto NO-GO below this
```

The drawdown check is NOT done here — it's done in `ExecuteTradeHandler` which has access to PeakEquity. The risk evaluator only checks: signal direction, confidence, and open positions count.

### RiskValidation Record Structure

```csharp
// src/ForexAI.Domain/ValueObjects/RiskValidation.cs
public record RiskValidation(
    string Decision,                     // "GO" | "NO-GO" | "GO_WITH_CAUTION"
    PositionDecision PositionDecision,
    TradeParameters? ValidatedParameters,
    List<string> CautionNotes,
    List<string> NoGoReasons)
{
    public bool IsGo => Decision == "GO" || Decision == "GO_WITH_CAUTION";
}
```

### Gate Implementation Pattern

```csharp
public async Task<RiskValidation> EvaluateAsync(
    TradeSignal signal, PredictorResult predictor,
    decimal equity, int openPositions)
{
    var noGoReasons = new List<string>();

    if (signal.Signal == SignalDirection.HOLD)
        noGoReasons.Add("Signal is HOLD — no actionable direction");

    if (predictor.AdjustedConfidence < 0.60m)
        noGoReasons.Add($"AI confidence {predictor.AdjustedConfidence:P0} below 60% minimum");

    if (openPositions >= 3)
        noGoReasons.Add($"Max open positions reached ({openPositions}/3)");

    if (noGoReasons.Count > 0)
        return new RiskValidation("NO-GO", PositionDecision.SKIP, null,
            new List<string>(), noGoReasons);

    var cautionNotes = new List<string>();
    if (predictor.AdjustedConfidence < 0.70m)
        cautionNotes.Add($"Confidence {predictor.AdjustedConfidence:P0} is valid but below 70% — trade with caution");

    var decision = cautionNotes.Count > 0 ? "GO_WITH_CAUTION" : "GO";

    return new RiskValidation(decision, PositionDecision.OPEN,
        signal.Parameters, cautionNotes, new List<string>());
}
```

Note: `PredictorResult` also has an `OverrideSignal` field. If `predictor.OverrideSignal` is set and differs from `signal.Signal`, log a warning — this is a safety note for future expansion (not blocking in current implementation).

### No Async I/O Needed

This evaluator is pure rule-based logic — no database or file I/O. The `Task` return type is just to satisfy the interface. Implementation can use `Task.FromResult(...)`.

### Project Structure Notes

New file:
```
src/ForexAI.Infrastructure/
└── Services/
    └── RuleBasedRiskEvaluator.cs    NEW
```

Update:
```
src/ForexAI.Infrastructure/DependencyInjection.cs   UPDATE — add IRiskEvaluator registration
```

### References

- [Source: src/ForexAI.Domain/Interfaces/IRiskEvaluator.cs]
- [Source: src/ForexAI.Domain/ValueObjects/RiskValidation.cs]
- [Source: src/ForexAI.Domain/ValueObjects/PredictorResult.cs]
- [Source: src/ForexAI.Domain/Enums/PositionDecision.cs]
- [Source: src/ForexAI.Domain/Enums/SignalDirection.cs]
- [Source: CLAUDE.md] — Hard risk limits section
- [Source: src/ForexAI.Application/UseCases/ExecuteTrade/ExecuteTradeHandler.cs] — drawdown check NOT in evaluator

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- ✅ `RiskValidation.ValidatedParameters` diubah ke nullable (`TradeParameters?`) di Domain — semantic benar untuk NO-GO case, `ExecuteTradeHandler` sudah guard dengan `IsGo` check sebelum akses field ini
- ✅ `ExecuteTradeHandler`: tambah `!` null-forgiving pada `ValidatedParameters!` — safe karena `IsGo` guard sudah ada di atas
- ✅ `PositionDecision.REJECT` digunakan untuk NO-GO (bukan `SKIP` yang tidak ada di enum)
- ✅ Pure `Task.FromResult(...)` — tidak ada I/O, sesuai design intent
- ✅ Build: 0 errors, 0 warnings

### File List

- `src/ForexAI.Domain/ValueObjects/RiskValidation.cs` — MODIFIED (`ValidatedParameters` → nullable)
- `src/ForexAI.Application/UseCases/ExecuteTrade/ExecuteTradeHandler.cs` — MODIFIED (null-forgiving `!` pada `ValidatedParameters`)
- `src/ForexAI.Infrastructure/Services/RuleBasedRiskEvaluator.cs` — NEW
- `src/ForexAI.Infrastructure/DependencyInjection.cs` — MODIFIED (tambah `IRiskEvaluator` registration)
