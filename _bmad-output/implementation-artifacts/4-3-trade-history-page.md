# Story 4.3: Trade History Page

Status: review

## Story

As a trader,
I want a `/history` page showing all past trades in a table with performance summary,
so that I can review my simulation performance over time.

## Acceptance Criteria

1. `src/app/history/page.tsx` exists and is accessible at `/history`
2. Table columns: Trade ID, Pair, Direction, Entry, SL, TP, Lot, Risk $, P&L $, Result, Opened, Closed
3. WIN rows: `text-emerald-600` for P&L and Result cells; LOSS rows: `text-red-600`
4. Direction badge: BUY = emerald Badge, SELL = red Badge (using shadcn/ui Badge)
5. Summary section above table: Total trades, Win count, Loss count, Win rate %, Total P&L $
6. Back navigation: "← Dashboard" link at top
7. Empty state: "No trades yet — approve your first signal to see history here"
8. Reads from `GET /api/positions` endpoint (returns all positions, not just active)

## Tasks / Subtasks

- [x] Add `getAllPositions(): Promise<TradePositionResponse[]>` to `src/lib/api.ts` (AC: 8)
  - [x] Backend: add `GET /api/positions` controller endpoint in `ForexAI.API` (returns all from repository)
  - [x] Frontend: call this endpoint on history page load
- [x] Create `src/app/history/page.tsx` (AC: 1)
  - [x] Fetch all positions on load
  - [x] Filter to only CLOSED_WIN and CLOSED_LOSS for main table
  - [x] Show SKIPPED separately or exclude (UX decision: exclude for cleaner history)
- [x] Build summary stats section (AC: 5)
- [x] Build table (AC: 2, 3, 4)
- [x] Back navigation link (AC: 6)
- [x] Empty state (AC: 7)

## Dev Notes

### Backend Prerequisite: GET /api/positions

This story requires adding one new endpoint to ForexAI.API. Add to `PositionController.cs`:

```csharp
[HttpGet]
public async Task<ActionResult<IReadOnlyList<TradePosition>>> GetAll(CancellationToken ct)
{
    var positions = await _mediator.Send(new GetAllPositionsQuery(), ct);
    return Ok(positions);
}
```

Also needs `GetAllPositionsQuery` + handler in Application layer. Keep it minimal:

```csharp
// ForexAI.Application/UseCases/GetAllPositions/
public record GetAllPositionsQuery : IRequest<IReadOnlyList<TradePosition>>;

public class GetAllPositionsHandler : IRequestHandler<GetAllPositionsQuery, IReadOnlyList<TradePosition>>
{
    // just calls ITradePositionRepository.GetAllAsync()
}
```

And add `GetAllAsync()` to `ITradePositionRepository` (Domain interface) + `JsonTradePositionRepository` implementation.

### Summary Stats Calculation

```typescript
const closedPositions = positions.filter(p =>
  p.status === 'CLOSED_WIN' || p.status === 'CLOSED_LOSS'
)
const wins = closedPositions.filter(p => p.status === 'CLOSED_WIN').length
const losses = closedPositions.filter(p => p.status === 'CLOSED_LOSS').length
const winRate = closedPositions.length > 0 ? (wins / closedPositions.length) * 100 : 0
const totalPnl = closedPositions.reduce((sum, p) => sum + p.floatingPnl, 0)
```

### Table Implementation

Use a simple `<table>` with Tailwind classes — no heavy table library needed for this simple use case:

```tsx
<table className="w-full text-sm">
  <thead>
    <tr className="border-b text-left text-xs text-muted-foreground uppercase tracking-wider">
      <th className="pb-2">Trade ID</th>
      {/* ... */}
    </tr>
  </thead>
  <tbody>
    {positions.map(p => (
      <tr key={p.tradeId} className="border-b hover:bg-muted/30">
        <td className="py-3 font-mono text-xs">{p.tradeId}</td>
        {/* ... */}
      </tr>
    ))}
  </tbody>
</table>
```

### Date Formatting

```typescript
function formatDate(iso: string | null): string {
  if (!iso) return '—'
  return new Intl.DateTimeFormat('id-ID', {
    dateStyle: 'short', timeStyle: 'short', timeZone: 'Asia/Jakarta'
  }).format(new Date(iso))
}
```

### References

- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Navigation Patterns]
- [Source: _bmad-output/implementation-artifacts/execution-log.json] — position data shape
- [Source: src/ForexAI.Domain/Interfaces/ITradePositionRepository.cs] — add GetAllAsync

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- Backend: `GetAllAsync()` added to `ITradePositionRepository` and `JsonTradePositionRepository` — loads all DTOs without status filter
- Backend: `GetAllPositionsQuery` + `GetAllPositionsHandler` created in `ForexAI.Application/UseCases/GetAllPositions/`
- Backend: `GET /api/position` endpoint added to `PositionController` — returns `IReadOnlyList<TradePosition>`; route kept under `/api/position` to match existing `GET /api/position/{pair}` pattern
- Backend `dotnet build` passes 0 errors, 0 warnings
- Frontend: `getAllPositions()` added to `api.ts` using `fetchApi('/api/position')`
- Frontend: `src/app/history/page.tsx` — client component with `useEffect` fetch on load
- Table: 12 columns per AC 2; WIN rows P&L + Result = `text-emerald-600`; LOSS = `text-red-600`; Direction uses shadcn `Badge` with emerald/red border per AC 4
- Summary: 5-card grid (Total Trades, Wins, Losses, Win Rate, Total P&L) with color-coded values
- SKIPPED positions excluded from table (UX decision: history shows meaningful closed trades only)
- Date format: `id-ID` locale, `Asia/Jakarta` timezone per dev notes pattern
- Empty state: "No trades yet — approve your first signal to see history here"
- Back link: `← Dashboard` using Next.js `Link` + `ArrowLeft` icon
- Frontend `npm run build` passes; `/history` route appears in build output

### File List

- src/ForexAI.Domain/Interfaces/ITradePositionRepository.cs
- src/ForexAI.Infrastructure/Persistence/Repositories/JsonTradePositionRepository.cs
- src/ForexAI.Application/UseCases/GetAllPositions/GetAllPositionsQuery.cs
- src/ForexAI.Application/UseCases/GetAllPositions/GetAllPositionsHandler.cs
- src/ForexAI.API/Controllers/PositionController.cs
- frontend/src/lib/api.ts
- frontend/src/app/history/page.tsx
- _bmad-output/implementation-artifacts/sprint-status.yaml
- _bmad-output/implementation-artifacts/4-3-trade-history-page.md
