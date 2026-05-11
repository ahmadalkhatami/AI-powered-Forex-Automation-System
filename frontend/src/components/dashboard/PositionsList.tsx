import { Card, CardContent } from '@/components/ui/card'
import { PositionCard } from '@/components/dashboard/PositionCard'
import type { PositionCardData } from '@/lib/types'

interface PositionsListProps {
  positions: PositionCardData[] | null
}

export function PositionsList({ positions }: PositionsListProps) {
  if (!positions || positions.length === 0) {
    return (
      <Card className="opacity-50">
        <CardContent className="p-4 text-center">
          <span className="text-sm text-muted-foreground">No open positions</span>
        </CardContent>
      </Card>
    )
  }

  return (
    <div className="space-y-2">
      <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground px-1">
        Open Positions ({positions.length}/3)
      </p>
      {positions.map((position) => (
        <PositionCard key={position.tradeId} position={position} />
      ))}
    </div>
  )
}
