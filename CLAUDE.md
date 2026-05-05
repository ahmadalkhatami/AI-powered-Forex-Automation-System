# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an **AI-powered Forex Automation System** — a semi-automated, simulation-first trading system for EUR/USD on M15/H1 timeframes. Built with Python, OANDA API, and MT5 bridge. Currently in simulation phase; risk management and backtesting take priority over live execution.

The project uses the **BMAD framework** (v6.6.0) to orchestrate AI agents and workflows. Custom forex skills live in `skills/` (not `.claude/skills/` which is the BMAD framework itself).

## Key Configuration

- **User:** AhmadAlkhatami
- **Communication language:** Indonesian (respond in Indonesian unless code/technical output)
- **User skill level:** Intermediate
- **Installed modules:** BMM (planning), TEA v1.15.1 (test architecture), BMB v1.7.0 (skill builder), Core

Config is layered — `_bmad/config.toml` (installer-managed, read-only) → `_bmad/custom/config.toml` (team overrides) → `_bmad/config.user.toml` + `_bmad/custom/config.user.toml` (personal overrides). Only edit the `custom/` files or `config.user.toml`.

## BMAD Skill Workflows

All work is done via slash commands (skills) in `.claude/skills/`. Invoke them with `/skill-name`.

### Core Planning Workflow (BMM Module)

Use these in order:
1. `/bmad-create-prd` — Generate Product Requirements Document (outputs to `_bmad-output/planning-artifacts/`)
2. `/bmad-create-architecture` — Design system architecture (outputs to `_bmad-output/planning-artifacts/`)
3. `/bmad-create-story` — Break work into implementable stories
4. `/bmad-dev-story` or `/bmad-quick-dev` — Implement a story

### Test Architecture Workflow (TEA Module — Agent: Murat)

`/bmad-testarch-<mode>` where mode is one of:
- `test-design` — Full test strategy and test case design
- `automate` — Generate automation scripts (Playwright, pytest, etc.)
- `atdd` — Acceptance Test-Driven Development scenarios
- `test-review` — Review existing tests
- `trace` — Build traceability matrix
- `framework` — Set up test framework scaffolding
- `ci` — Configure CI pipeline integration
- `nfr` — Non-functional requirements assessment

TEA config: auto-detects test stack/framework/CI platform; risk threshold = P1+; Playwright Utils enabled.

### Other Useful Skills

- `/bmad-agent-analyst` (Mary), `/bmad-agent-pm` (John), `/bmad-agent-architect` (Winston), `/bmad-agent-dev` (Amelia) — Invoke specific AI agents for their domain
- `/bmad-code-review` — Review generated or written code
- `/bmad-create-prd`, `/bmad-create-architecture`, `/bmad-create-story` — Core artifacts

## Output Structure

Generated artifacts land in:
```
_bmad-output/
├── planning-artifacts/       # PRDs, architecture docs, stories
├── implementation-artifacts/ # Code specs, API designs
└── test-artifacts/
    ├── test-design/
    ├── test-reviews/
    └── traceability/
```

Project knowledge documents go in `docs/` and feed back into subsequent skill executions.

## Forex Trading System — Custom Skills

The forex trading system is implemented as custom BMAD skills in `skills/`. All skills output JSON to `_bmad-output/` for pipeline interoperability.

### Pipeline Flow

```
/forex-market-analysis-signal
        ↓  signal-output.json
[forex-agent-ai-predictor — Zara: validate + confidence score]
        ↓  signal with predictor_validation
/forex-risk-management-gate
        ↓  risk-decision.json (GO / NO-GO / GO_WITH_CAUTION)
[forex-agent-execution — Axis: simulate or execute]
```

### Workflows (`skills/`)

| Skill | Invocation | Output |
|-------|-----------|--------|
| Market Analysis Signal | `/forex-market-analysis-signal` | `signal-output.json` |
| Risk Management Gate | `/forex-risk-management-gate` | `risk-decision.json` |

### Agents (`skills/`)

| Agent | Persona | Type | Role |
|-------|---------|------|------|
| `forex-agent-market-analyst` | Farida 📈 | Stateless | MA + RSI + S/R analysis → BUY/SELL/HOLD |
| `forex-agent-ai-predictor` | Zara 🤖 | Stateless | Confidence score (0–100), rule-based + LLM validation |
| `forex-agent-risk-manager` | Reza 🛡️ | Memory | Risk gate, lot sizing, drawdown tracking across sessions |
| `forex-agent-execution` | Axis ⚡ | Autonomous | Simulate/execute trades, monitor positions, PULSE for background monitoring |

### Hard Risk Limits (System Invariants)

- Risk per trade: **1%** of equity
- Max drawdown: **10%** → automatic system STOP
- Max open positions: **3**
- Minimum AI Predictor confidence: **60** → auto NO-GO below this

### Output Artifacts

```
_bmad-output/
├── planning-artifacts/
│   ├── signal-output.json      # From market analysis workflow
│   └── risk-decision.json      # From risk management gate
└── implementation-artifacts/
    ├── execution-log.json      # Trade execution history (Axis)
    └── position-status.json    # Active positions snapshot (Axis PULSE)
```

### Memory Agents — First Run

Reza and Axis are memory agents. On first activation they run **First Breath** — a calibration conversation to set risk profile and execution preferences. Run each once before using them in the pipeline:
- `/forex-agent-risk-manager` → Reza calibrates risk profile
- `/forex-agent-execution` → Axis configures execution mode

### MT5 Integration Status

Currently **SIMULATION ONLY**. MT5 Python library is Windows-only; for Mac use OANDA REST API or remote Windows VM. See `skills/forex-agent-execution/references/mt5-bridge.md` for integration plan.

## Extending BMAD (BMB Module)

To build new custom forex skills:
- New skill implementations → `skills/`
- Build reports → `skills/reports/`
- Use `/bmad-workflow-builder` or `/bmad-agent-builder` to create new skills

## Config Scripts

Two Python 3.11+ utility scripts (stdlib only, no install needed):
- `_bmad/scripts/resolve_config.py` — Resolves the 3-layer config merge
- `_bmad/scripts/resolve_customization.py` — Deep-merges TOML customizations

Lint custom skills with:
```bash
python3 .claude/skills/bmad-workflow-builder/scripts/prepass-workflow-integrity.py skills/<skill-name>
python3 .claude/skills/bmad-agent-builder/scripts/scan-path-standards.py skills/
```
