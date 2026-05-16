# Story 2.3: SignalHero Component

Status: review

## Story

As a trader,
I want to see the current signal direction, decision, and confidence at a glance without scrolling,
so that I can assess whether to approve or reject a trade in under 2 seconds.

## Acceptance Criteria

1. `SignalHero` component at `src/components/dashboard/SignalHero.tsx`
2. Five distinct states, each with different visual treatment:
   - `active-go`: Direction (BUY/SELL, `text-5xl font-black emerald-600`) + Decision ("GO ✓", `text-2xl font-bold emerald-600`) + confidence ProgressBar
   - `active-nogo`: Same layout but red; NO-GO reason badge visible below decision (no expand needed)
   - `active-caution`: GO_WITH_CAUTION, amber treatment; caution icon visible
   - `no-signal`: "No pending signal" centered, "Trigger New Analysis" ghost button
   - `monitoring`: "Signal processed — monitoring position" neutral state after approve
3. Pair + timeframe + timestamp shown as `text-sm text-muted-foreground`
4. `role="status"` + `aria-live="polite"` on root container
5. `onTriggerAnalysis` callback prop on `no-signal` button
6. Component accepts a `signal: SignalHeroData | null` prop — renders `no-signal` state when null
7. All 5 states render without TypeScript errors

## Tasks / Subtasks

- [x] Define `SignalHeroData` type in `src/lib/types.ts` (AC: 6)
  - [x] Fields: `id, pair, timeframe, signal, decision, confidenceScore, pair, timestamp, blockReason?, cautionNotes?`
- [x] Create `SignalHero.tsx` (AC: 1–5)
  - [x] Props interface: `{ signal: SignalHeroData | null; mode: 'monitoring' | 'default'; onTriggerAnalysis?: () => void }`
  - [x] State logic: derive `state` from `signal` and `mode` props
  - [x] `active-go` render
  - [x] `active-nogo` render with block reason badge
  - [x] `active-caution` render with amber treatment
  - [x] `no-signal` render with trigger button
  - [x] `monitoring` render
  - [x] `role="status"` + `aria-live="polite"`
- [x] Add to main page layout column (AC: 1)
- [x] Verify TypeScript (AC: 7)

## Dev Notes

### SignalHeroData Type

```typescript
// src/lib/types.ts
export type SignalDirection = 'BUY' | 'SELL' | 'HOLD'
export type DecisionType = 'GO' | 'NO-GO' | 'GO_WITH_CAUTION'

export interface SignalHeroData {
  id: string
  pair: string
  timeframe: string
  signal: SignalDirection
  decision: DecisionType
  confidenceScore: number       // 0.0–1.0
  confluenceScore: number
  timestamp: string             // ISO string
  blockReason?: string          // shown when NO-GO
  cautionNotes?: string[]       // shown when GO_WITH_CAUTION
}
```

### Component State Derivation

```typescript
type HeroState = 'active-go' | 'active-nogo' | 'active-caution' | 'no-signal' | 'monitoring'

function deriveState(
  signal: SignalHeroData | null,
  mode: 'monitoring' | 'default'
): HeroState {
  if (mode === 'monitoring') return 'monitoring'
  if (!signal) return 'no-signal'
  if (signal.decision === 'NO-GO') return 'active-nogo'
  if (signal.decision === 'GO_WITH_CAUTION') return 'active-caution'
  return 'active-go'
}
```

### Visual Treatment per State

**active-go:**
```tsx
<div role="status" aria-live="polite" className="...">
  <div className="flex items-center gap-6">
    <span className="text-5xl font-black text-emerald-600">{signal.signal}</span>
    <span className="text-2xl font-bold text-emerald-600">GO ✓</span>
  </div>
  <div className="flex items-center gap-3 mt-2">
    <span className="text-sm text-muted-foreground">{signal.pair} · {signal.timeframe}</span>
    <ProgressBar value={signal.confidenceScore * 100} color="emerald" className="w-32" />
    <span className="text-sm font-mono">{(signal.confidenceScore * 100).toFixed(0)}%</span>
  </div>
  <span className="text-xs text-muted-foreground">{formatTimestamp(signal.timestamp)}</span>
</div>
```

**active-nogo:**
Same structure but `text-red-600`, "NO-GO ✗", plus block reason:
```tsx
<Badge color="red" className="mt-2">{signal.blockReason}</Badge>
```

**no-signal:**
```tsx
<div className="flex flex-col items-center gap-3 py-8">
  <span className="text-muted-foreground">No pending signal</span>
  <Button variant="ghost" onClick={onTriggerAnalysis}>Trigger New Analysis</Button>
</div>
```

### Tremor ProgressBar Import

```typescript
import { ProgressBar } from '@tremor/react'
```

Tremor ProgressBar props: `value` (0–100), `color` ('emerald'|'red'|'amber'|...), `className`

### Timestamp Formatting

```typescript
function formatTimestamp(iso: string): string {
  return new Intl.DateTimeFormat('id-ID', {
    dateStyle: 'short', timeStyle: 'short', timeZone: 'Asia/Jakarta'
  }).format(new Date(iso))
}
```

### References

- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Component Strategy - SignalHero]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Defining Core Experience - Interaction Flow]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#User Journey Flows - Journey 1]
- UX spec: 5 states, `text-5xl font-black` for direction, `role="status"` + `aria-live`

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- No issues encountered — build passed on first attempt with zero TypeScript errors

### Completion Notes List

- `src/lib/types.ts` created with `SignalDirection`, `DecisionType`, and `SignalHeroData` interface
- `SignalHero.tsx` implements all 5 states: active-go (emerald), active-nogo (red + Badge), active-caution (amber + AlertTriangle icon + cautionNotes list), no-signal (ghost button), monitoring (neutral text)
- `deriveState()` pure function: monitoring mode overrides signal state; null signal → no-signal; decision drives active variant
- Tremor `ProgressBar` used for confidence score display (value 0–100, color matches state)
- Timestamp formatted with `Intl.DateTimeFormat` for id-ID locale, Asia/Jakarta timezone
- `role="status"` + `aria-live="polite"` on root container per AC: 4
- `onTriggerAnalysis` callback wired to ghost button in no-signal state per AC: 5
- Page renders `<SignalHero signal={null} mode="default" />` to show no-signal state as default
- `npm run build` passes with zero TypeScript or ESLint errors

### File List

- frontend/src/lib/types.ts
- frontend/src/components/dashboard/SignalHero.tsx
- frontend/src/app/page.tsx
- _bmad-output/implementation-artifacts/sprint-status.yaml
- _bmad-output/implementation-artifacts/2-3-signal-hero-component.md
