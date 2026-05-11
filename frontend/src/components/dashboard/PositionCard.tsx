import { Card, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import type { PositionCardData } from '@/lib/types'

const borderClasses = {
  ACTIVE: 'border-l-4 border-l-emerald-500',
  CLOSED_WIN: 'border-l-4 border-l-emerald-500 opacity-75',
  CLOSED_LOSS: 'border-l-4 border-l-red-500 opacity-75',
}

interface PositionCardProps {
  position: PositionCardData
}

export function PositionCard({ position }: PositionCardProps) {
  return (
    <Card className={cn(borderClasses[position.status])}>
      <CardContent className="p-3 space-y-2">
        {/* Header: Trade ID + Pair + Direction */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="text-xs text-muted-foreground font-mono">#{position.tradeId}</span>
            <span className="text-sm font-semibold">{position.pair}</span>
          </div>
          <Badge
            variant="outline"
            className={cn(
              'text-xs font-bold',
              position.direction === 'BUY'
                ? 'text-emerald-600 dark:text-emerald-400 border-emerald-300 dark:border-emerald-700'
                : 'text-red-600 dark:text-red-400 border-red-300 dark:border-red-700',
            )}
          >
            {position.direction}
          </Badge>
        </div>

        {/* Entry price */}
        <div className="flex items-center justify-between text-sm">
          <span className="text-muted-foreground">Entry</span>
          <span className="font-mono font-semibold">{position.entry.toFixed(4)}</span>
        </div>

        {/* Floating P&L */}
        <div className="flex items-center justify-between">
          <span className="text-sm text-muted-foreground">P&amp;L</span>
          <div className="flex items-baseline gap-1">
            <span
              className={cn(
                'font-mono font-bold text-lg',
                position.floatingPnl >= 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400',
              )}
            >
              {position.floatingPnl >= 0 ? '+' : ''}
              {position.floatingPnl.toFixed(2)}
            </span>
            <span className="text-xs text-muted-foreground">
              ({position.floatingPnlPips >= 0 ? '+' : ''}{position.floatingPnlPips} pips)
            </span>
          </div>
        </div>

        {/* Distance to SL/TP */}
        {(position.distanceToSlPips !== undefined || position.distanceToTpPips !== undefined) && (
          <div className="grid grid-cols-2 gap-2 text-xs">
            {position.distanceToSlPips !== undefined && (
              <div>
                <span className="text-muted-foreground">To SL </span>
                <span className="font-mono text-red-600 dark:text-red-400">{position.distanceToSlPips}p</span>
              </div>
            )}
            {position.distanceToTpPips !== undefined && (
              <div>
                <span className="text-muted-foreground">To TP </span>
                <span className="font-mono text-emerald-600 dark:text-emerald-400">{position.distanceToTpPips}p</span>
              </div>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
