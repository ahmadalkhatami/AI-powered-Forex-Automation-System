# Story 2.4: TradeParametersCard Component

Status: review

## Story

As a trader,
I want to see exact trade parameters (Entry, SL, TP, Lot, Risk) in a compact card using monospace numbers,
so that I can verify the numbers at a glance before approving a trade.

## Acceptance Criteria

1. `TradeParametersCard` at `src/components/dashboard/TradeParametersCard.tsx`
2. Displays when signal present: Entry, Stop Loss (with pips), Take Profit (with pips), Lot Size, Risk $ + %, R:R Ratio
3. Numbers use `font-mono font-bold text-2xl` — no exceptions
4. Stop Loss value: `text-red-600`; Take Profit value: `text-emerald-600`; Risk $: `text-amber-600`
5. Pip counts shown as `(-24 pips)` / `(+45 pips)` in `text-xs text-muted-foreground` inline
6. Empty/null state: renders a placeholder card with "No signal — parameters will appear here"
7. Component accepts `TradeParametersData | null` prop

## Tasks / Subtasks

- [x] Define `TradeParametersData` type in `src/lib/types.ts` (AC: 7)
- [x] Create `TradeParametersCard.tsx` (AC: 1–6)
  - [x] Card layout using shadcn/ui `Card` component
  - [x] 2-column label/value grid
  - [x] Color-coded value cells
  - [x] Pip suffix display
  - [x] Null/empty state
- [x] Add to main page layout (AC: 1)

## Dev Notes

### TradeParametersData Type

```typescript
export interface TradeParametersData {
  entry: number
  stopLoss: number
  stopLossPips: number
  takeProfit: number
  takeProfitPips: number
  lotSize: number
  riskAmount: number
  riskPercent: number
  potentialProfit: number
  riskRewardRatio: number
}
```

### Layout Pattern

```tsx
<Card>
  <CardHeader>
    <CardTitle className="text-sm font-semibold uppercase tracking-wider text-muted-foreground">
      TRADE PARAMETERS
    </CardTitle>
  </CardHeader>
  <CardContent>
    <div className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-3">
      <span className="text-xs text-muted-foreground self-center">Entry</span>
      <span className="font-mono font-bold text-2xl">{params.entry.toFixed(4)}</span>

      <span className="text-xs text-muted-foreground self-center">Stop Loss</span>
      <div className="flex items-baseline gap-2">
        <span className="font-mono font-bold text-2xl text-red-600">{params.stopLoss.toFixed(4)}</span>
        <span className="text-xs text-muted-foreground">(-{params.stopLossPips} pips)</span>
      </div>

      <span className="text-xs text-muted-foreground self-center">Take Profit</span>
      <div className="flex items-baseline gap-2">
        <span className="font-mono font-bold text-2xl text-emerald-600">{params.takeProfit.toFixed(4)}</span>
        <span className="text-xs text-muted-foreground">(+{params.takeProfitPips} pips)</span>
      </div>

      <span className="text-xs text-muted-foreground self-center">Lot Size</span>
      <span className="font-mono font-bold text-2xl">{params.lotSize.toFixed(2)}</span>

      <span className="text-xs text-muted-foreground self-center">Risk</span>
      <div className="flex items-baseline gap-2">
        <span className="font-mono font-bold text-2xl text-amber-600">${params.riskAmount.toFixed(2)}</span>
        <span className="text-xs text-muted-foreground">({params.riskPercent.toFixed(2)}%)</span>
      </div>

      <span className="text-xs text-muted-foreground self-center">R:R Ratio</span>
      <span className="font-mono font-bold text-2xl">1:{params.riskRewardRatio.toFixed(2)}</span>
    </div>
  </CardContent>
</Card>
```

### Number Formatting

- Price (4 decimal places): `.toFixed(4)` — `1.1268`
- Lot (2 decimal places): `.toFixed(2)` — `0.41`
- Money (2 decimal places): `$${n.toFixed(2)}` — `$98.40`
- Percentage: `${n.toFixed(2)}%` — `0.98%`
- R:R: `1:${n.toFixed(3)}` — `1:1.875`

### Empty State

```tsx
if (!params) {
  return (
    <Card className="opacity-50">
      <CardContent className="py-8 text-center text-sm text-muted-foreground">
        No signal — parameters will appear here
      </CardContent>
    </Card>
  )
}
```

### References

- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Component Strategy - TradeParametersCard]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Typography System]
- UX spec: `font-mono font-bold text-2xl` for all numbers — this is non-negotiable

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- No issues encountered — build passed on first attempt

### Completion Notes List

- `TradeParametersData` interface added to `src/lib/types.ts` (prepended before existing types)
- `TradeParametersCard.tsx` uses shadcn/ui `Card`/`CardHeader`/`CardContent`/`CardTitle` — `grid-cols-[auto_1fr]` for label/value alignment
- Color coding: SL value `text-red-600`, TP value `text-emerald-600`, Risk $ `text-amber-600` per AC: 4
- All numbers use `font-mono font-bold text-2xl` per AC: 3 (non-negotiable per UX spec)
- Pip counts inline with `text-xs text-muted-foreground`: `(-24 pips)` / `(+45 pips)` per AC: 5
- R:R uses 3 decimal places: `1:1.875` per dev notes
- Null state: `opacity-50` card with "No signal — parameters will appear here" per AC: 6
- Page renders `<TradeParametersCard params={null} />` in main column below SignalHero
- `npm run build` passes with zero TypeScript or ESLint errors

### File List

- frontend/src/lib/types.ts
- frontend/src/components/dashboard/TradeParametersCard.tsx
- frontend/src/app/page.tsx
- _bmad-output/implementation-artifacts/sprint-status.yaml
- _bmad-output/implementation-artifacts/2-4-trade-parameters-card.md
