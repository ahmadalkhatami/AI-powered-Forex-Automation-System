# Story 3.3: RiskGatePanel Component

Status: review

## Story

As a trader,
I want to expand a collapsible Risk Gate panel showing Reza's validation result,
so that I understand exactly why a trade is GO, GO_WITH_CAUTION, or NO-GO before acting.

## Acceptance Criteria

1. `RiskGatePanel` at `src/components/dashboard/RiskGatePanel.tsx`
2. Uses shadcn/ui `Collapsible` — **collapsed by default**
3. Header (always visible): "RISK GATE" label + GO/NO-GO badge (always visible — key info above the fold)
4. Expanded content — three variants:
   - **GO**: account status section (equity, drawdown %, open positions count), trade parameters validation summary, empty caution list
   - **GO_WITH_CAUTION**: same as GO + amber `CautionNotes` list with `⚠` prefix per item
   - **NO-GO**: card gets `border-red-300` border, `NoGoReasons` displayed as red `✗` list items; account section still shown
5. Drawdown % shown with Tremor ProgressBar; color changes: emerald < 7%, amber 7–9%, red ≥ 9%
6. Accepts `RiskGatePanelData | null` prop

## Tasks / Subtasks

- [x] Define `RiskGatePanelData` type in `src/lib/types.ts` (AC: 6)
- [x] Create `RiskGatePanel.tsx` (AC: 1–5)
  - [x] Collapsible wrapper with Card
  - [x] Header: label + decision badge
  - [x] Expanded GO state
  - [x] Expanded GO_WITH_CAUTION state (GO + caution notes)
  - [x] Expanded NO-GO state (red border + no-go reasons)
  - [x] Drawdown ProgressBar with dynamic color
- [x] Add to main page layout column (AC: 1)

## Dev Notes

### RiskGatePanelData Type

```typescript
export interface RiskGatePanelData {
  decision: 'GO' | 'NO-GO' | 'GO_WITH_CAUTION'
  equity: number
  drawdownPct: number       // 0.0–1.0
  openPositions: number
  maxPositions: number
  cautionNotes: string[]
  noGoReasons: string[]
  validatedEntry?: number
  validatedStopLoss?: number
  validatedTakeProfit?: number
}
```

### Card Border Variant for NO-GO

```tsx
<Card className={cn(
  "transition-colors",
  data.decision === 'NO-GO' && "border-red-300 dark:border-red-800"
)}>
```

### Drawdown Progress Color Logic

```typescript
function drawdownColor(pct: number): string {
  if (pct >= 0.09) return 'red'
  if (pct >= 0.07) return 'amber'
  return 'emerald'
}
```

### Decision Badge

```tsx
const badgeColors = {
  'GO': 'emerald',
  'GO_WITH_CAUTION': 'amber',
  'NO-GO': 'red',
} as const

const badgeLabels = {
  'GO': 'GO ✓',
  'GO_WITH_CAUTION': 'GO ⚠',
  'NO-GO': 'NO-GO ✗',
} as const
```

Use Tremor `Badge` for consistent color variants.

### Account Status Section

```tsx
<div className="grid grid-cols-3 gap-4 p-4 bg-muted/30 rounded-md">
  <div>
    <p className="text-xs text-muted-foreground">Equity</p>
    <p className="font-mono font-bold">${data.equity.toLocaleString()}</p>
  </div>
  <div>
    <p className="text-xs text-muted-foreground">Drawdown</p>
    <div className="flex items-center gap-1">
      <ProgressBar value={data.drawdownPct * 100} color={drawdownColor(data.drawdownPct)} className="flex-1" />
      <span className="text-xs font-mono">{(data.drawdownPct * 100).toFixed(1)}%</span>
    </div>
  </div>
  <div>
    <p className="text-xs text-muted-foreground">Positions</p>
    <p className="font-mono font-bold">{data.openPositions}/{data.maxPositions}</p>
  </div>
</div>
```

### NO-GO Reasons List

```tsx
{data.decision === 'NO-GO' && (
  <div className="space-y-1 p-4">
    {data.noGoReasons.map((reason, i) => (
      <p key={i} className="text-sm text-red-600 flex items-start gap-2">
        <span>✗</span>
        <span>{reason}</span>
      </p>
    ))}
  </div>
)}
```

### References

- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Component Strategy - RiskGatePanel]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#User Journey Flows - Journey 3 NO-GO]
- [Source: _bmad-output/planning-artifacts/risk-decision.json] — actual risk decision data shape

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- First build failed: `CardHeader` imported but unused — removed from import, second build passed

### Completion Notes List

- `RiskGatePanelData` interface added to `src/lib/types.ts`
- `RiskGatePanel.tsx` wraps `Card` + `Collapsible` — collapsed by default (`open` initialized to `false`)
- Header trigger shows Tremor `Badge` with color-coded decision: GO (emerald), GO_WITH_CAUTION (amber), NO-GO (red)
- `Card` gets `border-red-300 dark:border-red-800` when decision is NO-GO per AC: 4
- Account status: 3-column grid (Equity, Drawdown + ProgressBar, Positions open/max)
- `drawdownColor()`: emerald <7%, amber 7–9%, red ≥9% per AC: 5
- NO-GO reasons rendered as red `✗` list; caution notes as amber `⚠` list — mutually exclusive
- Validated parameters (entry/SL/TP) shown if provided in data
- Null state: opacity-50 placeholder card
- Tremor `Badge` used per dev notes (supports color prop natively)
- `npm run build` passes with zero TypeScript or ESLint errors

### File List

- frontend/src/lib/types.ts
- frontend/src/components/dashboard/RiskGatePanel.tsx
- frontend/src/app/page.tsx
- _bmad-output/implementation-artifacts/sprint-status.yaml
- _bmad-output/implementation-artifacts/3-3-risk-gate-panel.md
