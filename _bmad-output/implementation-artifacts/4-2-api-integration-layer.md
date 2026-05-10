# Story 4.2: API Integration Layer

Status: ready-for-dev

## Story

As a frontend developer,
I want a typed API client that connects the dashboard to the ForexAI .NET backend,
so that all dashboard components display real pipeline data instead of hardcoded fixtures.

## Acceptance Criteria

1. `src/lib/types.ts` contains all TypeScript types mirroring C# domain models
2. `src/lib/api.ts` with typed async functions:
   - `analyzeSignal(pair, timeframe): Promise<TradeSignalResponse>`
   - `evaluateRisk(req: EvaluateRiskRequest): Promise<RiskValidationResponse>`
   - `executeTrade(req: ExecuteTradeRequest): Promise<TradePositionResponse>`
   - `getPositionStatus(pair): Promise<TradePositionResponse | null>`
3. Base URL from `NEXT_PUBLIC_API_URL` env var (default: `http://localhost:5000`)
4. Main dashboard page fetches signal + position status on load using React state
5. APPROVE button calls `executeTrade` on confirm; position card appears immediately in sidebar after success
6. REJECT button calls a local dismiss (no API call needed — signal is just removed from state)
7. "Trigger New Analysis" button calls `analyzeSignal`, then `evaluateRisk` with the result
8. Toast notifications fire on position opened/closed/stopped events

## Tasks / Subtasks

- [ ] Complete `src/lib/types.ts` with all response shapes (AC: 1)
- [ ] Create `src/lib/api.ts` with 4 functions (AC: 2, 3)
  - [ ] Base URL from env var
  - [ ] Error handling: throw typed errors with HTTP status
- [ ] Create `src/lib/env.ts` with `NEXT_PUBLIC_API_URL` export
- [ ] Update `src/app/page.tsx` with data fetching logic (AC: 4)
  - [ ] `useState` for signal, riskValidation, positions, accountHealth
  - [ ] `useEffect` for initial load (analyzeSignal + getPositionStatus)
  - [ ] Pass real data to all child components
- [ ] Wire APPROVE confirm → executeTrade → position card appears (AC: 5)
- [ ] Wire REJECT confirm → dismiss from state (AC: 6)
- [ ] Wire "Trigger New Analysis" → full pipeline (AC: 7)
- [ ] Wire toast notifications (AC: 8)

## Dev Notes

### API Response Types (matching C# JSON output)

```typescript
// src/lib/types.ts
export interface TradeSignalResponse {
  id: string
  runId: string
  pair: string
  timeframe: string
  signal: 'BUY' | 'SELL' | 'HOLD'
  confluenceScore: number
  confidenceScore: number
  snapshot: MarketSnapshotResponse
  trend: TrendAnalysisResponse
  momentum: MomentumAnalysisResponse
  structure: StructureAnalysisResponse
  parameters: TradeParametersResponse
  warnings: string[]
  timestamp: string
}

export interface RiskValidationResponse {
  decision: 'GO' | 'NO-GO' | 'GO_WITH_CAUTION'
  positionDecision: 'OPEN' | 'SKIP'
  isGo: boolean
  validatedParameters: TradeParametersResponse | null
  cautionNotes: string[]
  noGoReasons: string[]
}

export interface TradePositionResponse {
  tradeId: string
  runId: string
  status: 'ACTIVE' | 'SKIPPED' | 'CLOSED_WIN' | 'CLOSED_LOSS'
  pair: string
  direction: 'BUY' | 'SELL' | 'HOLD'
  entry: number
  stopLoss: number
  takeProfit: number
  lotSize: number
  riskAmount: number
  potentialProfit: number
  riskReward: number
  floatingPnl: number
  floatingPnlPips: number
  openedAt: string | null
  closedAt: string | null
  mode: string
  skipReason: string | null
}
```

### api.ts Pattern

```typescript
// src/lib/api.ts
const BASE_URL = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000'

async function fetchApi<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`API ${res.status}: ${text}`)
  }
  return res.json() as Promise<T>
}

export async function analyzeSignal(pair: string, timeframe: string): Promise<TradeSignalResponse> {
  return fetchApi('/api/signal/analyze', {
    method: 'POST',
    body: JSON.stringify({ pair, timeframe }),
  })
}

export async function getPositionStatus(pair: string): Promise<TradePositionResponse | null> {
  const res = await fetch(`${BASE_URL}/api/position/${pair}`)
  if (res.status === 204) return null
  if (!res.ok) throw new Error(`API ${res.status}`)
  return res.json()
}
```

### Page State Machine

```typescript
// src/app/page.tsx
'use client'

type PageState = 'loading' | 'no-signal' | 'signal-ready' | 'processing' | 'monitoring' | 'error'

export default function DashboardPage() {
  const [pageState, setPageState] = useState<PageState>('loading')
  const [signal, setSignal] = useState<TradeSignalResponse | null>(null)
  const [riskValidation, setRiskValidation] = useState<RiskValidationResponse | null>(null)
  const [activePosition, setActivePosition] = useState<TradePositionResponse | null>(null)

  // Initial load: get current signal + position
  useEffect(() => { /* fetch */ }, [])

  const handleApprove = async () => {
    setPageState('processing')
    const position = await executeTrade({ ... })
    setActivePosition(position)
    setPageState('monitoring')
    toast.success(`${position.tradeId} active`)
  }
  // ...
}
```

### Environment Variable Setup

Create `frontend/.env.local`:
```
NEXT_PUBLIC_API_URL=http://localhost:5000
```

Create `frontend/.env.example`:
```
NEXT_PUBLIC_API_URL=http://localhost:5000
```

### Toast Library

shadcn/ui `toast` (already installed in Story 2.1). Use the `useToast` hook:

```typescript
import { useToast } from '@/components/ui/use-toast'

const { toast } = useToast()
toast({ title: "Position opened", description: `${tradeId} active` })
toast({ title: "Position closed", description: `+${pips} pips`, variant: "default" })
```

### References

- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#User Journey Flows - Journey 1]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#UX Consistency Patterns - Feedback Patterns]
- [Source: src/ForexAI.API/Controllers/] — API endpoint shapes (from Story 1.4)
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md - Project Structure `lib/api.ts`]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

### File List
