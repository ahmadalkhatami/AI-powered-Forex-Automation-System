# Epics — AI-powered Forex Automation System

**Project:** ForexAI — Semi-automated, simulation-first trading dashboard  
**Stack:** C# .NET 8 (Clean Architecture backend) + React/Next.js 14 (frontend)  
**Date:** 2026-05-10

---

## Current Implementation State

### Already Complete (do NOT re-implement)
- `ForexAI.Domain`: All value objects, entities, interfaces, enums — ✅ DONE
- `ForexAI.Application`: All 4 use cases (AnalyzeSignal, EvaluateRisk, ExecuteTrade, GetPositionStatus) + DI — ✅ DONE

### To Be Built
- `ForexAI.Infrastructure`: Repository + service implementations
- `ForexAI.API`: Controllers, Program.cs, DI wiring
- `Frontend`: React/Next.js dashboard per UX specification

---

## Epic 1: Infrastructure & API Layer

**Goal:** Complete the C# backend by implementing infrastructure layer (repository + service stubs) and exposing the pipeline via a minimal REST API.

**Business value:** Enables end-to-end pipeline execution and provides the API endpoints the frontend will consume.

### Story 1.1: JSON File-Based Repositories

**As a** backend developer,  
**I want** working implementations of `ITradePositionRepository` and `ISignalRepository` that persist to JSON files,  
**so that** the Application layer use cases can store and retrieve trade data without a database.

**Acceptance Criteria:**

1. `JsonTradePositionRepository` implements `ITradePositionRepository` in `ForexAI.Infrastructure/Repositories/`
   - Reads/writes to `_bmad-output/implementation-artifacts/execution-log.json`
   - `GetActiveByPairAsync(string pair)` returns the active position for given pair or null
   - `GetOpenPositionsAsync()` returns all positions with Status = ACTIVE
   - `SaveAsync(TradePosition)` appends or updates position in JSON file (upsert by TradeId)
   - `CountOpenPositionsAsync()` returns count of active positions
2. `JsonSignalRepository` implements `ISignalRepository` in `ForexAI.Infrastructure/Repositories/`
   - Reads/writes to `_bmad-output/implementation-artifacts/signal-history.json`
   - `GetLatestAsync(string pair)` returns most recent signal for pair or null
   - `GetByIdAsync(Guid id)` returns signal by ID or null
   - `SaveAsync(TradeSignal)` appends signal to JSON array
3. Both repositories handle file-not-found (return empty/null, create file on first write)
4. `dotnet build` passes with zero errors for `ForexAI.Infrastructure`

**Technical Requirements:**
- Use `System.Text.Json` — NOT Newtonsoft.Json
- File paths resolved relative to `Directory.GetCurrentDirectory()` — not hardcoded absolute paths
- `JsonSerializerOptions` with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
- Custom `JsonConverter` needed for `DateTimeOffset` and `decimal` if serialization issues arise
- Thread-safety not required (single-user simulation tool, no concurrency)

**Source hints:**
- Domain interfaces: `src/ForexAI.Domain/Interfaces/ITradePositionRepository.cs`
- Domain interfaces: `src/ForexAI.Domain/Interfaces/ISignalRepository.cs`
- Domain entities: `src/ForexAI.Domain/Entities/TradePosition.cs`, `TradeSignal.cs`
- Existing JSON format reference: `_bmad-output/implementation-artifacts/execution-log.json`

---

### Story 1.2: Market Data Stub and Signal Analyzer

**As a** backend developer,  
**I want** stub implementations of `IMarketDataService` and `ISignalAnalyzer` that return realistic fixture data,  
**so that** the AnalyzeSignal use case can run end-to-end in simulation without a live data feed.

**Acceptance Criteria:**

1. `StubMarketDataService` implements `IMarketDataService`
   - `GetSnapshotAsync(pair, timeframe)` returns a `MarketSnapshot` built from `_bmad-output/planning-artifacts/signal-output.json`
   - Returns the same fixture data for any pair/timeframe combination in simulation mode
2. `BmadSignalAnalyzer` implements `ISignalAnalyzer`
   - `AnalyzeAsync(MarketSnapshot)` reads `signal-output.json` and maps it to a `TradeSignal` entity
   - Maps all score_breakdown fields to the correct value object properties
   - Maps analysis.trend → TrendAnalysis, analysis.momentum → MomentumAnalysis, analysis.structure → StructureAnalysis
3. `dotnet build` passes with zero errors

**Source hints:**
- `src/ForexAI.Domain/Interfaces/IMarketDataService.cs`
- `src/ForexAI.Domain/Interfaces/ISignalAnalyzer.cs`
- `src/ForexAI.Domain/ValueObjects/MarketSnapshot.cs`, `TrendAnalysis.cs`, `MomentumAnalysis.cs`, `StructureAnalysis.cs`
- Fixture data: `_bmad-output/planning-artifacts/signal-output.json`

---

### Story 1.3: Rule-Based Risk Evaluator

**As a** backend developer,  
**I want** a rule-based implementation of `IRiskEvaluator` that enforces the hard risk limits,  
**so that** the EvaluateRisk use case applies the same logic as Reza (the risk management agent).

**Acceptance Criteria:**

1. `RuleBasedRiskEvaluator` implements `IRiskEvaluator` in `ForexAI.Infrastructure/Services/`
2. `EvaluateAsync(signal, predictor, equity, openPositions)` returns `RiskValidation` with:
   - Decision = "NO-GO" if `openPositions >= 3`
   - Decision = "NO-GO" if `predictor.AdjustedConfidence < 0.60`
   - Decision = "NO-GO" if `signal.Signal == HOLD`
   - Decision = "GO" if all gates pass
   - Decision = "GO_WITH_CAUTION" if GO but `predictor.AdjustedConfidence < 0.70`
3. When GO/GO_WITH_CAUTION: `ValidatedParameters` is populated from `signal.Parameters`
4. When NO-GO: `NoGoReasons` list is populated with human-readable reason strings
5. `dotnet build` passes with zero errors

**Source hints:**
- `src/ForexAI.Domain/Interfaces/IRiskEvaluator.cs`
- `src/ForexAI.Domain/ValueObjects/RiskValidation.cs`, `PredictorResult.cs`
- Hard limits defined in `CLAUDE.md` system invariants section

---

### Story 1.4: API Controllers and Program.cs Wiring

**As a** frontend developer,  
**I want** a minimal REST API with 4 endpoints wired up to MediatR,  
**so that** the React frontend can trigger analysis, evaluate risk, execute trades, and query position status.

**Acceptance Criteria:**

1. `Program.cs` in `ForexAI.API`:
   - Registers `ForexAI.Application` via `services.AddApplication()`
   - Registers `ForexAI.Infrastructure` via `services.AddInfrastructure()`
   - Adds CORS for `http://localhost:3000` (Next.js dev server)
   - Swagger/OpenAPI enabled in Development environment
2. `ForexAI.Infrastructure/DependencyInjection.cs` registers:
   - `JsonTradePositionRepository` as `ITradePositionRepository` (scoped)
   - `JsonSignalRepository` as `ISignalRepository` (scoped)
   - `StubMarketDataService` as `IMarketDataService` (scoped)
   - `BmadSignalAnalyzer` as `ISignalAnalyzer` (scoped)
   - `RuleBasedRiskEvaluator` as `IRiskEvaluator` (scoped)
3. Controllers in `ForexAI.API/Controllers/`:
   - `POST /api/signal/analyze` → `AnalyzeSignalCommand` → returns `TradeSignal` JSON
   - `POST /api/risk/evaluate` → `EvaluateRiskCommand` → returns `RiskValidation` JSON
   - `POST /api/trade/execute` → `ExecuteTradeCommand` → returns `TradePosition` JSON
   - `GET /api/position/{pair}` → `GetPositionStatusQuery` → returns `TradePosition?` JSON
4. `dotnet run` starts without errors, Swagger UI accessible at `/swagger`

**Source hints:**
- `src/ForexAI.Application/DependencyInjection.cs` (pattern to follow for Infrastructure)
- `src/ForexAI.Application/UseCases/` (all 4 commands/queries)

---

### Story 1.5: End-to-End Pipeline Smoke Test

**As a** developer,  
**I want** a simple integration test that runs the full pipeline via the API,  
**so that** I can verify Domain + Application + Infrastructure + API all wire up correctly.

**Acceptance Criteria:**

1. Manual test script or xUnit integration test that calls:
   - `POST /api/signal/analyze` with `{ "pair": "EURUSD", "timeframe": "M15" }`
   - Takes returned SignalId and calls `POST /api/risk/evaluate`
   - If GO: calls `POST /api/trade/execute`
   - Calls `GET /api/position/EURUSD`
2. All 4 calls return HTTP 200 with valid JSON bodies
3. A `TradePosition` with `Status: "ACTIVE"` is returned from execute endpoint
4. `execution-log.json` is created/updated in `_bmad-output/implementation-artifacts/`

---

## Epic 2: Frontend Foundation

**Goal:** Set up the React/Next.js 14 project and build Phase 1 components — the minimal set needed to complete the Golden Path (Signal Review → Approve).

**Design reference:** `_bmad-output/planning-artifacts/ux-design-specification.md`  
**Direction:** A — Signal Command (Stockbit-inspired, 2-column desktop-first)

### Story 2.1: Next.js 14 Project Setup

**As a** frontend developer,  
**I want** a properly configured Next.js 14 App Router project with Tailwind CSS, shadcn/ui, and Tremor installed,  
**so that** I can build components using the agreed design system without configuration friction.

**Acceptance Criteria:**

1. `frontend/` directory at project root containing a Next.js 14 App Router project
2. Tailwind CSS 3.4+ configured with custom color tokens:
   - `signal-buy: emerald-600/400`, `signal-sell: red-600/400`, `signal-hold: slate-500/400`
   - `decision-go: emerald-600`, `decision-nogo: red-600`, `simulation-banner: amber-400`
3. shadcn/ui initialized (`npx shadcn-ui@latest init`), components in `components/ui/`
4. Tremor 3.x installed
5. Fonts: Inter (UI) + JetBrains Mono (numbers) via `next/font/google`
6. `npm run dev` starts on port 3000 with no errors
7. `npm run build` completes with no TypeScript errors

**Technical Requirements:**
- TypeScript strict mode enabled
- `src/` directory layout (not root-level `app/`)
- Path alias `@/` → `src/`

---

### Story 2.2: Layout Shell (ModeBanner + Header)

**As a** trader,  
**I want** the dashboard to immediately show SIMULATION vs LIVE mode and display the currency pair + timeframe in the header,  
**so that** I never mistake the mode I'm operating in.

**Acceptance Criteria:**

1. `ModeBanner` component (`src/components/layout/ModeBanner.tsx`):
   - Simulation variant: `bg-amber-400` full-width, text "⚠ SIMULATION MODE — No real trades are executed"
   - Live variant: `bg-red-700 text-white`, text "🔴 LIVE MODE — Trades execute with real capital"
   - `role="alert"` on the banner element
   - Not dismissable
2. `Header` component (`src/components/layout/Header.tsx`):
   - Shows "EUR/USD · M15" as title
   - History link (ghost button, navigates to `/history`)
   - Dark mode toggle icon button
3. Root layout (`src/app/layout.tsx`) includes ModeBanner above Header above `{children}`
4. 2-column grid layout on `xl` breakpoint (65%/35%), single column below `md`

---

### Story 2.3: SignalHero Component

**As a** trader,  
**I want** to see the current signal direction, decision, and confidence at a glance without scrolling,  
**so that** I can assess the trade in under 2 seconds.

**Acceptance Criteria:**

1. `SignalHero` component (`src/components/dashboard/SignalHero.tsx`):
   - `active-go` state: Direction (BUY/SELL, `text-5xl font-black emerald-600`) + Decision (GO ✓, `text-2xl font-bold`) + Confidence ProgressBar
   - `active-nogo` state: Same but red, NO-GO badge with block reason visible
   - `active-caution` state: GO_WITH_CAUTION, amber treatment
   - `no-signal` state: "No pending signal" with "Trigger New Analysis" ghost button
   - `monitoring` state: "Signal processed — monitoring position"
2. Pair + timeframe + timestamp shown in `text-sm text-muted-foreground`
3. `role="status"` + `aria-live="polite"` on container
4. All 5 states visually verified in Storybook or standalone test page

---

### Story 2.4: TradeParametersCard Component

**As a** trader,  
**I want** to see exact trade parameters (Entry, SL, TP, Lot, Risk) in a compact card,  
**so that** I can verify the numbers before approving a trade.

**Acceptance Criteria:**

1. `TradeParametersCard` component (`src/components/dashboard/TradeParametersCard.tsx`):
   - Displays: Entry, Stop Loss (with pips), Take Profit (with pips), Lot Size, Risk $, R:R ratio
   - Numbers use `font-mono font-bold text-2xl`
   - Stop Loss value colored `text-red-600`, Take Profit `text-emerald-600`
   - Risk $ colored `text-amber-600`
   - Empty state when no signal present
2. Pips shown as `(-24 pips)` / `(+45 pips)` in `text-sm text-muted-foreground`

---

## Epic 3: Frontend Actions & Analysis Panels

**Goal:** Complete the decision-support UI — the collapsible analysis panels and the APPROVE/REJECT action area.

### Story 3.1: ApproveRejectActions Component

**As a** trader,  
**I want** prominent APPROVE and REJECT buttons with a confirm dialog,  
**so that** I can execute or dismiss a trade signal safely without accidental clicks.

**Acceptance Criteria:**

1. `ApproveRejectActions` component (`src/components/dashboard/ApproveRejectActions.tsx`):
   - `enabled-go`: APPROVE green `bg-emerald-600`, REJECT outlined red
   - `enabled-caution`: APPROVE amber + inline caution text below
   - `disabled-nogo`: APPROVE `opacity-50 cursor-not-allowed aria-disabled="true"`, REJECT still enabled
   - `processing`: APPROVE shows spinner, buttons non-interactive
2. APPROVE AlertDialog: "Confirm trade: BUY EUR/USD @ 1.1268, risking $98.40 (0.98%) — SIMULATION"
3. REJECT AlertDialog: "Reject this signal? It will be dismissed."
4. APPROVE always left, REJECT always right
5. Both use shadcn/ui `AlertDialog` — not native `confirm()`

---

### Story 3.2: SignalAnalysisPanel Component

**As a** trader,  
**I want** to expand a collapsible panel showing the signal analysis breakdown (trend, momentum, structure),  
**so that** I can verify the AI reasoning before approving without being forced to read it every time.

**Acceptance Criteria:**

1. `SignalAnalysisPanel` component (`src/components/dashboard/SignalAnalysisPanel.tsx`):
   - Uses shadcn/ui `Collapsible` — collapsed by default
   - Header shows panel title + confidence badge (always visible)
   - Expanded: 3 score rows (Trend, Momentum, Structure) each with ProgressBar + percentage + rationale text
   - Predictor score (83/100) and agreement score shown
   - Warnings displayed as amber `⚠` list items
2. `aria-expanded` and `aria-controls` wired correctly
3. Keyboard toggle works (Enter/Space on header)

---

### Story 3.3: RiskGatePanel Component

**As a** trader,  
**I want** to expand a collapsible Risk Gate panel showing Reza's validation result,  
**so that** I understand why a trade is GO or NO-GO.

**Acceptance Criteria:**

1. `RiskGatePanel` component (`src/components/dashboard/RiskGatePanel.tsx`):
   - Uses shadcn/ui `Collapsible` — collapsed by default
   - Header: "RISK GATE" + GO/NO-GO badge (always visible)
   - Expanded (GO): account status (equity, drawdown, positions), trade parameters validation, caution notes
   - Expanded (NO-GO): red border on card, `NoGoReasons` displayed as red list items
   - Expanded (GO_WITH_CAUTION): GO treatment + amber `CautionNotes` list
2. NO-GO state: card gets `border-red-300 bg-red-50 dark:bg-red-950/20` treatment

---

## Epic 4: Frontend Sidebar, Integration & Polish

**Goal:** Complete the sidebar components, wire up real API data, and add dark mode + responsive polish.

### Story 4.1: AccountHealthBar and PositionCard Components

**As a** trader,  
**I want** to see my account health and active positions in the sidebar at all times,  
**so that** I have ambient awareness of my trading state without it interrupting the main decision flow.

**Acceptance Criteria:**

1. `AccountHealthBar` component (`src/components/dashboard/AccountHealthBar.tsx`):
   - Shows: Equity $, Drawdown % with ProgressBar, Open Positions count/max
   - States: `normal` (< 7%), `warning` (7–9% amber), `critical` (9–10% red), `stopped` (≥ 10% blocked)
2. `PositionCard` component (`src/components/dashboard/PositionCard.tsx`):
   - Shows: Trade ID, Pair + direction badge, Entry price, Floating P&L ($ and pips), distance to SL/TP
   - States: `active` (emerald border), `closed-win` (emerald border, final P&L), `closed-loss` (red border)
3. Both components in sidebar column of 2-column layout

---

### Story 4.2: API Integration Layer

**As a** frontend developer,  
**I want** a typed API client (`lib/api.ts`) that connects the dashboard to the ForexAI .NET backend,  
**so that** all dashboard components display real pipeline data instead of hardcoded fixtures.

**Acceptance Criteria:**

1. `src/lib/api.ts` with typed functions:
   - `analyzeSignal(pair, timeframe): Promise<TradeSignal>`
   - `evaluateRisk(signalId, predictor, equity, openPositions): Promise<RiskValidation>`
   - `executeTrade(signalId, riskValidation, mode): Promise<TradePosition>`
   - `getPositionStatus(pair): Promise<TradePosition | null>`
2. TypeScript types in `src/lib/types.ts` mirroring C# domain models
3. Base URL from `NEXT_PUBLIC_API_URL` env var (default: `http://localhost:5000`)
4. Main dashboard page (`src/app/page.tsx`) fetches signal + position status on load
5. APPROVE button calls `executeTrade` on confirm; position card appears immediately after

---

### Story 4.3: Trade History Page

**As a** trader,  
**I want** a `/history` page showing all past trades in a table,  
**so that** I can review my simulation performance over time.

**Acceptance Criteria:**

1. `src/app/history/page.tsx` with a table showing all closed positions
2. Columns: Trade ID, Pair, Direction, Entry, SL, TP, Lot, Risk $, Result (WIN/LOSS), P&L $, Opened, Closed
3. WIN rows have `text-emerald-600`, LOSS rows have `text-red-600`
4. Back navigation to main dashboard
5. Empty state: "No trades yet — approve your first signal to see history here"

---

### Story 4.4: Dark Mode, Responsive Polish & Accessibility Audit

**As a** trader,  
**I want** the dashboard to work well on tablets and in dark mode,  
**so that** I can check positions on any device without visual strain.

**Acceptance Criteria:**

1. Dark mode toggle (`Header`) switches between light/dark using shadcn/ui theme system
2. All custom color tokens have `dark:` variants configured in Tailwind
3. Layout collapses to single column at `md` breakpoint
4. APPROVE/REJECT area becomes `fixed bottom-0` sticky bar on mobile (`< sm`)
5. All interactive elements have `min-h-[44px]` touch targets
6. `axe-core` browser extension shows 0 violations on main dashboard
7. Keyboard-only navigation: full golden path completable without mouse
