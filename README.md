# AI-powered Forex Automation System

> Semi-automated EUR/USD trading system with AI signal analysis, rule-based risk management, and a human-approval dashboard. **Simulation-first** — no live capital at risk until you flip the switch.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Build](https://img.shields.io/badge/build-passing-brightgreen)
![Epic 1](https://img.shields.io/badge/Epic%201%20Backend-review-blue)
![Mode](https://img.shields.io/badge/mode-SIMULATION-orange)
![License](https://img.shields.io/badge/license-MIT-green)

---

## What it does

The system runs a **5-stage pipeline** for every trade decision:

```
Market Analysis  →  AI Predictor  →  Risk Gate  →  Human Approval  →  Execution
   (Farida 📈)      (Zara 🤖)       (Reza 🛡️)                       (Axis ⚡)
```

1. **Farida** analyzes MA crossovers, RSI, and support/resistance on M15 + H1
2. **Zara** validates the signal, scores confidence (0–100), flags concerns
3. **Reza's risk gate** enforces hard limits (1% risk/trade, max 3 positions, min 60% confidence)
4. **You** approve or reject via the React dashboard
5. **Axis** executes the trade (simulation or live via OANDA/MT5)

---

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    React Dashboard (Epic 2–4)                 │
│                    Next.js · TypeScript · Tailwind            │
└────────────────────────┬─────────────────────────────────────┘
                         │ HTTP / REST
┌────────────────────────▼─────────────────────────────────────┐
│                    ForexAI.API (.NET 8)                       │
│   POST /signal/analyze  ·  POST /risk/evaluate                │
│   POST /trade/execute   ·  GET  /position/{pair}              │
└────────────────────────┬─────────────────────────────────────┘
                         │ MediatR CQRS
┌────────────────────────▼─────────────────────────────────────┐
│                 ForexAI.Application                           │
│   AnalyzeSignalHandler  ·  EvaluateRiskHandler                │
│   ExecuteTradeHandler   ·  GetPositionStatusHandler           │
└────────────┬──────────────────────────┬──────────────────────┘
             │ Domain interfaces        │
┌────────────▼────────────┐  ┌──────────▼──────────────────────┐
│    ForexAI.Domain        │  │    ForexAI.Infrastructure        │
│  Entities · ValueObjects │  │  JsonRepositories · Services     │
│  Interfaces · Enums      │  │  BmadSignalAnalyzer              │
└─────────────────────────┘  └──────────────────────────────────┘
```

Clean Architecture — dependency arrows point inward. Domain knows nothing about infrastructure.

---

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Claude Code (for BMAD skill workflows)

### Run the API

```bash
git clone <repo> && cd AI-powered-Forex-Automation-System

dotnet build ForexAI.sln
dotnet run --project src/ForexAI.API

# Swagger UI → http://localhost:5000/swagger
```

The repo ships with pre-generated EUR/USD fixture data in `_bmad-output/planning-artifacts/` so the API works immediately — no live market connection needed.

### Run Tests

```bash
dotnet test ForexAI.sln
```

```
Test Run Successful.
Total tests: 1
     Passed: 1   ← full pipeline smoke test
```

---

## Project Status

| Epic | Description | Status |
|------|-------------|--------|
| **Epic 1** | Infrastructure & API Layer | ✅ Review |
| **Epic 2** | Frontend Foundation (Next.js) | 🔜 Ready |
| **Epic 3** | Frontend Actions & Analysis Panels | 🔜 Ready |
| **Epic 4** | Frontend Sidebar, Integration & Polish | 🔜 Ready |

### Epic 1 Stories

| Story | Description | Status |
|-------|-------------|--------|
| 1.1 | JSON File Repositories | ✅ |
| 1.2 | Market Data Stub + Signal Analyzer | ✅ |
| 1.3 | Rule-Based Risk Evaluator | ✅ |
| 1.4 | API Controllers + Program.cs Wiring | ✅ |
| 1.5 | End-to-End Pipeline Smoke Test | ✅ |

---

## Hard Risk Limits

These are **system invariants** enforced in code. They cannot be bypassed via the API.

| Rule | Value | Where enforced |
|------|-------|---------------|
| Risk per trade | **1%** of equity | `ExecuteTradeHandler` |
| Max drawdown | **10%** → STOP | `ExecuteTradeHandler` |
| Max open positions | **3** | `RuleBasedRiskEvaluator` + `ExecuteTradeHandler` |
| Minimum AI confidence | **60%** | `RuleBasedRiskEvaluator` |

---

## Repository Structure

```
├── src/
│   ├── ForexAI.Domain/          # Entities, value objects, interfaces
│   ├── ForexAI.Application/     # MediatR use cases
│   ├── ForexAI.Infrastructure/  # JSON persistence, stub services
│   └── ForexAI.API/             # REST controllers, Program.cs
├── tests/
│   └── ForexAI.Integration/     # WebApplicationFactory smoke test
├── _bmad-output/
│   ├── planning-artifacts/      # PRD, architecture, JSON fixtures
│   └── implementation-artifacts/# Story files, sprint status
├── skills/                      # Custom BMAD AI agent skills
├── docs/                        # Wiki
│   ├── architecture.md
│   ├── pipeline.md
│   ├── api-reference.md
│   └── development.md
└── ForexAI.sln
```

---

## BMAD AI Workflow

This project uses the **BMAD framework** for AI-assisted development. All stories are tracked in `_bmad-output/implementation-artifacts/sprint-status.yaml`.

```bash
/bmad-dev-story        # implement next ready-for-dev story
/bmad-create-story     # create story file from epics
/bmad-code-review      # review completed work
```

Custom forex skills in `skills/`:

```bash
/forex-market-analysis-signal   # run Farida → signal-output.json
/forex-risk-management-gate     # run Reza  → risk-decision.json
```

---

## Docs

- [Architecture](docs/architecture.md) — Clean Architecture layers, design decisions
- [Pipeline](docs/pipeline.md) — Stage-by-stage trade flow
- [API Reference](docs/api-reference.md) — Endpoint specs, request/response shapes
- [Development Guide](docs/development.md) — Setup, testing, adding features

---

## License

MIT
