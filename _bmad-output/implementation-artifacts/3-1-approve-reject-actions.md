# Story 3.1: ApproveRejectActions Component

Status: ready-for-dev

## Story

As a trader,
I want prominent APPROVE and REJECT buttons with confirmation dialogs,
so that I can execute or dismiss a trade signal safely without accidental clicks.

## Acceptance Criteria

1. `ApproveRejectActions` at `src/components/dashboard/ApproveRejectActions.tsx`
2. Four states:
   - `enabled-go`: APPROVE `bg-emerald-600 text-white`, REJECT outlined red — both enabled
   - `enabled-caution`: APPROVE `bg-amber-500 text-white` + inline caution text below, REJECT enabled
   - `disabled-nogo`: APPROVE `opacity-50 cursor-not-allowed aria-disabled="true"` — visually greyed but NOT `disabled` attribute; REJECT enabled
   - `processing`: APPROVE shows spinner, both non-interactive
3. APPROVE AlertDialog text: "Confirm trade: [BUY/SELL] [PAIR] @ [ENTRY], risking $[RISK] ([PCT]%) — SIMULATION"
4. REJECT AlertDialog text: "Reject this signal? It will be dismissed and pipeline will await next trigger."
5. APPROVE always left, REJECT always right — fixed layout, never reversed
6. AlertDialog cannot be dismissed with Escape key — must click Cancel/Keep Signal explicitly
7. `onApprove` and `onReject` callbacks fire only after dialog confirmation
8. Component accepts `ActionState` prop and `signal: SignalHeroData` for dialog text population

## Tasks / Subtasks

- [ ] Define `ActionState` type (AC: 2)
- [ ] Create `ApproveRejectActions.tsx` (AC: 1–7)
  - [ ] Derive button visual state from `ActionState` prop
  - [ ] APPROVE button with AlertDialog
  - [ ] REJECT button with AlertDialog
  - [ ] `aria-disabled` on APPROVE when NO-GO (not HTML `disabled`)
  - [ ] Spinner in APPROVE when processing
  - [ ] Caution text below APPROVE when caution state
  - [ ] `onApprove` / `onReject` callback props
- [ ] Add to main page layout (AC: 1)

## Dev Notes

### ActionState Type

```typescript
export type ActionState = 'enabled-go' | 'enabled-caution' | 'disabled-nogo' | 'processing'
```

### CRITICAL: `aria-disabled` vs `disabled` attribute

Per UX spec: "APPROVE button **disabled, not hidden** — transparent system; Ahmad knows it exists but system blocks it"

Use `aria-disabled="true"` + CSS `opacity-50 cursor-not-allowed pointer-events-none` — NOT the HTML `disabled` attribute. This ensures screen readers can still discover the button and explain why it's unavailable.

```tsx
<Button
  className={cn(
    "bg-emerald-600 text-white ...",
    state === 'disabled-nogo' && "opacity-50 cursor-not-allowed pointer-events-none"
  )}
  aria-disabled={state === 'disabled-nogo'}
  onClick={state === 'disabled-nogo' ? undefined : () => setApproveOpen(true)}
>
  APPROVE
</Button>
```

### AlertDialog — Prevent Escape Dismiss

shadcn/ui AlertDialog uses Radix UI under the hood. To prevent Escape dismiss:

```tsx
<AlertDialog open={approveOpen} onOpenChange={setApproveOpen}>
  <AlertDialogContent onEscapeKeyDown={(e) => e.preventDefault()}>
    ...
  </AlertDialogContent>
</AlertDialog>
```

### APPROVE AlertDialog Content

```tsx
<AlertDialogContent>
  <AlertDialogHeader>
    <AlertDialogTitle>Confirm Trade</AlertDialogTitle>
    <AlertDialogDescription>
      {signal.signal} {signal.pair} @ {signal.parameters?.entry.toFixed(4)}
      <br />
      Stop Loss: {signal.parameters?.stopLoss.toFixed(4)} | Take Profit: {signal.parameters?.takeProfit.toFixed(4)}
      <br />
      <span className="font-semibold text-amber-600">
        Risk: ${signal.parameters?.riskAmount.toFixed(2)} ({signal.parameters?.riskPercent.toFixed(2)}% equity) — SIMULATION
      </span>
    </AlertDialogDescription>
  </AlertDialogHeader>
  <AlertDialogFooter>
    <AlertDialogCancel>Cancel</AlertDialogCancel>
    <AlertDialogAction
      className="bg-emerald-600 hover:bg-emerald-700"
      onClick={onApprove}
    >
      Confirm Trade ▶
    </AlertDialogAction>
  </AlertDialogFooter>
</AlertDialogContent>
```

### REJECT AlertDialog Content

```tsx
<AlertDialogContent>
  <AlertDialogHeader>
    <AlertDialogTitle>Reject Signal</AlertDialogTitle>
    <AlertDialogDescription>
      This signal will be dismissed. The pipeline will await the next analysis trigger.
    </AlertDialogDescription>
  </AlertDialogHeader>
  <AlertDialogFooter>
    <AlertDialogCancel>Keep Signal</AlertDialogCancel>
    <AlertDialogAction
      className="border border-red-300 text-red-600 bg-transparent hover:bg-red-50"
      onClick={onReject}
    >
      Reject
    </AlertDialogAction>
  </AlertDialogFooter>
</AlertDialogContent>
```

### Caution Text When GO_WITH_CAUTION

```tsx
{state === 'enabled-caution' && signal.cautionNotes?.map((note, i) => (
  <p key={i} className="text-xs text-amber-600 mt-1">⚠ {note}</p>
))}
```

### References

- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Component Strategy - ApproveRejectActions]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Modal & Overlay Patterns]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#User Journey Flows - Journey 3 NO-GO]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

### File List
