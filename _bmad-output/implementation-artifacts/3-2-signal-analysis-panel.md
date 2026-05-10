# Story 3.2: SignalAnalysisPanel Component

Status: ready-for-dev

## Story

As a trader,
I want to expand a collapsible panel showing the signal analysis breakdown (trend, momentum, structure, predictor),
so that I can verify AI reasoning before approving without being forced to read it every time.

## Acceptance Criteria

1. `SignalAnalysisPanel` at `src/components/dashboard/SignalAnalysisPanel.tsx`
2. Uses shadcn/ui `Collapsible` — **collapsed by default**
3. Header (always visible): "SIGNAL ANALYSIS" label + confidence badge
4. Expanded content shows:
   - Three score rows: Trend, Momentum, Structure — each with: label, ProgressBar (Tremor), score %, rationale text
   - Predictor summary: score (83/100) and agreement score
   - Warnings list: amber `⚠` items from `signal.warnings`
5. `aria-expanded` state wired correctly to Collapsible trigger
6. Keyboard toggle: Enter/Space on header opens/closes panel
7. Accepts `SignalAnalysisData | null` prop — renders placeholder when null

## Tasks / Subtasks

- [ ] Define `SignalAnalysisData` type in `src/lib/types.ts` (AC: 7)
- [ ] Create `SignalAnalysisPanel.tsx` (AC: 1–6)
  - [ ] Collapsible with collapse-by-default
  - [ ] Header row with trigger + confidence badge
  - [ ] Three score rows (Trend, Momentum, Structure)
  - [ ] Predictor summary row
  - [ ] Warnings list
  - [ ] Aria attributes
- [ ] Add to main page layout column (AC: 1)

## Dev Notes

### SignalAnalysisData Type

```typescript
export interface SignalAnalysisData {
  trendScore: number          // 0.0–1.0
  trendBias: string           // "Bullish"
  trendStrength: string       // "Sedang"
  trendRationale: string
  momentumScore: number
  momentumRSI: number
  momentumDirection: string   // "Naik"
  momentumRationale: string
  structureScore: number
  structurePattern: string    // "Support flip zone"
  structureRationale: string
  predictorScore: number      // 0–100 (e.g. 83)
  agreementScore: number      // 0.0–1.0
  confidenceScore: number     // 0.0–1.0 (for header badge)
  warnings: string[]
}
```

### Collapsible Pattern (shadcn/ui)

```tsx
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'

<Collapsible defaultOpen={false}>
  <CollapsibleTrigger className="flex w-full items-center justify-between p-4 hover:bg-muted/50">
    <span className="text-sm font-semibold uppercase tracking-wider">SIGNAL ANALYSIS</span>
    <div className="flex items-center gap-2">
      <Badge color="blue">{(data.confidenceScore * 100).toFixed(0)}% confidence</Badge>
      <ChevronDown className="h-4 w-4 transition-transform [[data-state=open]_&]:rotate-180" />
    </div>
  </CollapsibleTrigger>
  <CollapsibleContent>
    {/* score rows */}
  </CollapsibleContent>
</Collapsible>
```

The `[[data-state=open]_&]:rotate-180` Tailwind selector rotates the chevron when open — Radix sets `data-state="open"` on the trigger automatically.

### Score Row Pattern

```tsx
function ScoreRow({ label, score, subtitle, rationale }: ScoreRowProps) {
  return (
    <div className="grid grid-cols-[80px_1fr_40px] gap-3 items-center py-2">
      <span className="text-xs text-muted-foreground font-medium">{label}</span>
      <div className="space-y-1">
        <ProgressBar value={score * 100} color={scoreColor(score)} />
        <p className="text-xs text-muted-foreground">{subtitle}</p>
      </div>
      <span className="text-sm font-mono font-bold text-right">{(score * 100).toFixed(0)}%</span>
    </div>
  )
}

function scoreColor(score: number): string {
  if (score >= 0.70) return 'emerald'
  if (score >= 0.50) return 'amber'
  return 'red'
}
```

### Rationale Text

Show rationale text as `text-xs text-muted-foreground` collapsed text — show first 80 chars with expand toggle, or show full text if it fits in 2 lines. Keep it simple: just show full text, no further nested expand needed.

### Aria: Collapsible Trigger

shadcn/ui `CollapsibleTrigger` already handles `aria-expanded` via Radix UI — no manual wiring needed. Just use the component as-is.

### References

- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Component Strategy - SignalAnalysisPanel]
- [Source: _bmad-output/planning-artifacts/signal-output.json] — actual score breakdown format
- [Source: _bmad-output/planning-artifacts/predictor-decision.json] — predictor score fields

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

### File List
