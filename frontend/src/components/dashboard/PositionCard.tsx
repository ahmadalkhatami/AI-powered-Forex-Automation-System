'use client'

import { useState } from 'react'
import { Card, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import type { PositionCardData } from '@/lib/types'

const borderClasses = {
  ACTIVE: 'border-l-4 border-l-emerald-500',
  CLOSED_WIN: 'border-l-4 border-l-emerald-500 opacity-75',
  CLOSED_LOSS: 'border-l-4 border-l-red-500 opacity-75',
}

interface PositionCardProps {
  position: PositionCardData
  currentPrice?: number
  onCloseMarket?: (tradeId: string) => Promise<void>
}

export function PositionCard({ position, currentPrice: _currentPrice, onCloseMarket }: PositionCardProps) {
  const [confirming, setConfirming] = useState(false)
  const [closing, setClosing] = useState(false)

  const handleClose = async () => {
    if (!onCloseMarket) return
    setClosing(true)
    try {
      await onCloseMarket(position.tradeId)
    } finally {
      setClosing(false)
      setConfirming(false)
    }
  }

  return (
    <Card className={cn(borderClasses[position.status])}>
      <CardContent className="p-3 space-y-2">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="text-xs text-muted-foreground font-mono">#{position.tradeId.slice(-12)}</span>
            <span className="text-sm font-semibold">{position.pair}</span>
          </div>
          <div className="flex items-center gap-1.5">
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
            {position.status === 'CLOSED_WIN' && (
              <Badge className="bg-emerald-500/20 text-emerald-600 dark:text-emerald-400 text-xs border-0">WIN</Badge>
            )}
            {position.status === 'CLOSED_LOSS' && (
              <Badge className="bg-red-500/20 text-red-600 dark:text-red-400 text-xs border-0">LOSS</Badge>
            )}
          </div>
        </div>

        {/* Entry price */}
        <div className="flex items-center justify-between text-sm">
          <span className="text-muted-foreground">Entry</span>
          <span className="font-mono font-semibold">{position.entry.toFixed(5)}</span>
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

        {/* Closed at */}
        {position.closedAt && (
          <p className="text-xs text-muted-foreground">
            Closed: {new Date(position.closedAt).toLocaleString('id-ID', {
              day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit'
            })}
          </p>
        )}

        {/* One-click close — backend auto-detect WIN/LOSS dari floating P&L + pakai market price */}
        {position.status === 'ACTIVE' && onCloseMarket && (
          <div className="pt-1 border-t border-border/50">
            {!confirming ? (
              <Button
                variant="outline"
                size="sm"
                className="w-full h-7 text-xs"
                onClick={() => setConfirming(true)}
                disabled={closing}
              >
                Close at Market
              </Button>
            ) : (
              <div className="flex gap-1.5">
                <Button
                  size="sm"
                  className={cn(
                    'flex-1 h-7 text-xs',
                    position.floatingPnl >= 0
                      ? 'bg-emerald-600 hover:bg-emerald-700'
                      : 'bg-red-600 hover:bg-red-700',
                  )}
                  onClick={handleClose}
                  disabled={closing}
                  title={`Close akan eksekusi market & catat ${position.floatingPnl >= 0 ? 'WIN' : 'LOSS'} dari floating P&L`}
                >
                  {closing
                    ? 'Closing…'
                    : `Confirm: close as ${position.floatingPnl >= 0 ? 'WIN' : 'LOSS'} (${position.floatingPnl >= 0 ? '+' : ''}${position.floatingPnl.toFixed(2)})`}
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-7 text-xs px-2"
                  onClick={() => setConfirming(false)}
                  disabled={closing}
                >
                  ✕
                </Button>
              </div>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
