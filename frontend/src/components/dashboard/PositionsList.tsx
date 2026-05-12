import { Card, CardContent } from '@/components/ui/card'
import { PositionCard } from '@/components/dashboard/PositionCard'
import type { PositionCardData } from '@/lib/types'

interface PositionsListProps {
  positions: PositionCardData[] | null
  currentPrice?: number
  onClosePosition?: (tradeId: string, outcome: 'WIN' | 'LOSS', exitPrice: number) => Promise<void>
}

export function PositionsList({ positions, currentPrice, onClosePosition }: PositionsListProps) {
  const active = positions?.filter((p) => p.status === 'ACTIVE') ?? []
  const closed = positions?.filter((p) => p.status !== 'ACTIVE') ?? []

  if (!positions || positions.length === 0) {
    return (
      <Card className="opacity-50">
        <CardContent className="p-4 text-center">
          <span className="text-sm text-muted-foreground">No positions yet</span>
        </CardContent>
      </Card>
    )
  }

  return (
    <div className="space-y-2">
      {active.length > 0 && (
        <>
          <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground px-1">
            Open Positions ({active.length}/3)
          </p>
          {active.map((position) => (
            <PositionCard
              key={position.tradeId}
              position={position}
              currentPrice={currentPrice}
              onClose={onClosePosition}
            />
          ))}
        </>
      )}
      {closed.length > 0 && (
        <>
          <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground px-1 pt-2">
            History ({closed.length})
          </p>
          {closed.map((position) => (
            <PositionCard key={position.tradeId} position={position} />
          ))}
        </>
      )}
    </div>
  )
}
