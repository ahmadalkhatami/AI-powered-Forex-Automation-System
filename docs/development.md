# Development Guide

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | **8.0+** | `dotnet --version` |
| Node.js | 20+ | For Epic 2–4 frontend |
| Git | any | |

---

## Getting Started

```bash
git clone <repo>
cd AI-powered-Forex-Automation-System

# Build entire solution
dotnet build ForexAI.sln

# Run API
dotnet run --project src/ForexAI.API
# → http://localhost:5000/swagger
```

### Required Planning Artifacts

The stub services read from BMAD output files. Before running the API, generate them via the BMAD skills:

```bash
# In Claude Code:
/forex-market-analysis-signal    # → _bmad-output/planning-artifacts/signal-output.json
/forex-risk-management-gate      # → _bmad-output/planning-artifacts/risk-decision.json
```

These files are committed to the repo (current EUR/USD M15 fixture), so `dotnet run` works out of the box.

---

## Project Structure

```
AI-powered-Forex-Automation-System/
├── src/
│   ├── ForexAI.Domain/             # Entities, value objects, interfaces, enums
│   ├── ForexAI.Application/        # MediatR use cases, DI registration
│   ├── ForexAI.Infrastructure/     # Repositories, services, DI registration
│   └── ForexAI.API/                # Controllers, request models, Program.cs
├── tests/
│   └── ForexAI.Integration/        # WebApplicationFactory pipeline smoke test
├── _bmad-output/
│   ├── planning-artifacts/         # PRD, architecture, signal/risk JSON fixtures
│   └── implementation-artifacts/  # Story files, sprint-status.yaml, execution-log.json
├── skills/                         # Custom BMAD forex skills
├── docs/                           # This wiki
├── ForexAI.sln                     # Solution file
└── CLAUDE.md                       # AI agent instructions
```

---

## Running Tests

```bash
# All tests
dotnet test ForexAI.sln

# Integration tests only (verbose)
dotnet test tests/ForexAI.Integration/ --logger "console;verbosity=normal"
```

The integration test (`PipelineIntegrationTests`) runs the full 4-step pipeline against an in-memory test host:

```
POST /api/signal/analyze  →  200 (BUY signal)
POST /api/risk/evaluate   →  200 (GO_WITH_CAUTION)
POST /api/trade/execute   →  200 (ACTIVE position)
GET  /api/position/EURUSD →  200 (same position)
```

> Each test run appends a new position to `execution-log.json`. This is expected behavior.

---

## Adding a New Use Case

1. **Domain** — add interface to `src/ForexAI.Domain/Interfaces/` if new infrastructure needed
2. **Application** — add `CommandName.cs` + `CommandHandler.cs` under `src/ForexAI.Application/UseCases/NewFeature/`
3. **Infrastructure** — implement any new interfaces, register in `DependencyInjection.cs`
4. **API** — add endpoint to existing controller or create new one

MediatR is auto-registered via `services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))` — no manual handler registration needed.

---

## BMAD Workflow

Stories are managed via the BMAD framework:

```bash
# Check sprint status
cat _bmad-output/implementation-artifacts/sprint-status.yaml

# Create next story
/bmad-create-story

# Implement story
/bmad-dev-story
```

Story files live in `_bmad-output/implementation-artifacts/` as `{epic}-{story}-{name}.md`.

---

## Risk Management — Hard Limits

These limits are enforced in `ExecuteTradeHandler` and `RuleBasedRiskEvaluator`. **Never bypass them.**

| Invariant | Value |
|-----------|-------|
| Risk per trade | **1%** of equity |
| Max drawdown | **10%** → system STOP |
| Max open positions | **3** |
| Min AI confidence | **60%** |

---

## Simulation Mode

All trades run with `mode: "SIMULATION"` until MT5 or OANDA integration is complete. Trade IDs use prefix `SIM-{date}-{seq}`. Live mode will use `LIVE-` prefix.

See [pipeline.md](pipeline.md) for the full execution flow and [architecture.md](architecture.md) for layer responsibilities.
