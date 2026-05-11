# Story 4.1: AccountHealthBar and PositionCard Components

Status: review

## Story

As a trader,
I want to see my account health and active positions in the sidebar at all times,
so that I have ambient awareness of my trading state without it interrupting the main decision flow.

## Acceptance Criteria

1. `AccountHealthBar` at `src/components/dashboard/AccountHealthBar.tsx`:
   - Shows Equity $, Drawdown % with ProgressBar, Open Positions count/max
   - Four states: `normal` (< 7%), `warning` (7–9% amber), `critical` (9–10% red), `stopped` (≥ 10% blocked with red banner)
2. `PositionCard` at `src/components/dashboard/PositionCard.tsx`:
   - Shows: Trade ID, Pair + Direction badge, Entry price, Floating P&L ($ and pips), Distance to SL/TP
   - Three states: `active` (emerald left border), `closed-win` (emerald border + final P&L), `closed-loss` (red border + final P&L)
3. Both components placed in sidebar column of 2-column layout
4. Sidebar renders multiple `PositionCard`s when multiple positions exist (max 3 per hard limit)
5. Empty positions state: "No open positions" placeholder card

## Tasks / Subtasks

- [x] Define `AccountHealthData` and `PositionCardData` types in `src/lib/types.ts` (AC: 1, 2)
- [x] Create `AccountHealthBar.tsx` (AC: 1)
  - [x] Equity display
  - [x] Drawdown ProgressBar with dynamic color
  - [x] Positions count
  - [x] `stopped` state with red warning banner inside card
- [x] Create `PositionCard.tsx` (AC: 2)
  - [x] Trade ID + Pair + Direction badge header
  - [x] Entry price display
  - [x] Floating P&L with sign color (positive = emerald, negative = red)
  - [x] Distance to SL/TP in pips
  - [x] Status-based border color
- [x] Create `PositionsList.tsx` wrapper that handles empty state and multiple cards (AC: 4, 5)
- [x] Add both to sidebar column in `src/app/page.tsx` (AC: 3)

## Dev Notes

### AccountHealthData Type

```typescript
export interface AccountHealthData {
  equity: number
  peakEquity: number
  drawdownPct: number     // 0.0–1.0
  openPositions: number
  maxPositions: number    // always 3
}

type AccountHealthState = 'normal' | 'warning' | 'critical' | 'stopped'

function deriveHealthState(drawdownPct: number): AccountHealthState {
  if (drawdownPct >= 0.10) return 'stopped'
  if (drawdownPct >= 0.09) return 'critical'
  if (drawdownPct >= 0.07) return 'warning'
  return 'normal'
}
```

### PositionCardData Type

```typescript
export interface PositionCardData {
  tradeId: string
  pair: string
  direction: 'BUY' | 'SELL'
  entry: number
  currentPrice?: number
  floatingPnl: number       // in USD
  floatingPnlPips: number
  distanceToSlPips?: number
  distanceToTpPips?: number
  status: 'ACTIVE' | 'CLOSED_WIN' | 'CLOSED_LOSS'
  closedAt?: string
}
```

### PositionCard Border Variants

```typescript
const borderClasses = {
  ACTIVE: 'border-l-4 border-l-emerald-500',
  CLOSED_WIN: 'border-l-4 border-l-emerald-500 opacity-75',
  CLOSED_LOSS: 'border-l-4 border-l-red-500 opacity-75',
}
```

### Floating P&L Color

```tsx
<span className={cn(
  "font-mono font-bold text-lg",
  position.floatingPnl >= 0 ? "text-emerald-600" : "text-red-600"
)}>
  {position.floatingPnl >= 0 ? '+' : ''}{position.floatingPnl.toFixed(2)}
</span>
<span className="text-xs text-muted-foreground ml-1">
  ({position.floatingPnlPips >= 0 ? '+' : ''}{position.floatingPnlPips} pips)
</span>
```

### AccountHealthBar Stopped State

```tsx
{state === 'stopped' && (
  <div className="bg-red-100 dark:bg-red-950/30 border border-red-300 rounded-md p-2 mt-2">
    <p className="text-xs text-red-600 font-semibold">⛔ SYSTEM STOP — 10% drawdown limit reached</p>
    <p className="text-xs text-red-500">New trades blocked until equity recovers</p>
  </div>
)}
```

### References

- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Component Strategy - AccountHealthBar]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Component Strategy - PositionCard]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#User Journey Flows - Journey 4]
- [Source: CLAUDE.md] — max open positions = 3, max drawdown = 10%

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- `AccountHealthData` and `PositionCardData` interfaces added to `src/lib/types.ts`
- `AccountHealthBar.tsx`: 4-state health display (normal/warning/critical/stopped) using `deriveHealthState()`; Tremor ProgressBar with dynamic color; stopped state shows red warning banner
- `PositionCard.tsx`: border-l-4 variant per status (ACTIVE/CLOSED_WIN/CLOSED_LOSS); floating P&L with sign prefix and pip count; optional SL/TP distance display
- `PositionsList.tsx`: wrapper with "No open positions" empty state and "Open Positions (N/3)" label
- Added `AccountHealthBar` and `PositionsList` to sidebar column in `page.tsx`; replaced placeholder text
- `npm run build` passes with zero TypeScript or ESLint errors

### File List

- frontend/src/lib/types.ts
- frontend/src/components/dashboard/AccountHealthBar.tsx
- frontend/src/components/dashboard/PositionCard.tsx
- frontend/src/components/dashboard/PositionsList.tsx
- frontend/src/app/page.tsx
- _bmad-output/implementation-artifacts/sprint-status.yaml
- _bmad-output/implementation-artifacts/4-1-account-health-position-card.md
