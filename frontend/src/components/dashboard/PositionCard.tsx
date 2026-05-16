'use client'

import { useMemo, useState } from 'react'
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
  bid?: number          // MIFX bid — BUY exit price (sell back at bid)
  ask?: number          // MIFX ask — SELL exit price (buy back at ask)
  lastTickAt?: string   // ISO timestamp of last MIFX tick; > STALE_MS old → fallback to DB
  onCloseMarket?: (tradeId: string) => Promise<void>
}

// Kalau tick terakhir > 30 detik lalu, anggap stale → jangan interpolasi
const STALE_MS = 30_000

/**
 * Estimate PnL dari live MIFX bid/ask. Match formula broker:
 *  - BUY exit at bid:  priceDelta = bid - entry
 *  - SELL exit at ask: priceDelta = entry - ask
 * Pakai bid/ask (bukan mid) supaya selaras dengan PnL MIFX yang sudah include spread.
 *
 * Fallback ke position.floatingPnl (server-synced) kalau:
 *  - Tick missing / stale (> 30s)
 *  - Status bukan ACTIVE
 *  - Lot size 0 (data corrupt)
 */
function interpolatePnl(
  position: PositionCardData,
  bid: number | undefined,
  ask: number | undefined,
  lastTickAt: string | undefined,
) {
  if (position.status !== 'ACTIVE' || !position.lotSize) {
    return { pnl: position.floatingPnl, pips: position.floatingPnlPips, isLive: false }
  }

  const stale = !lastTickAt || (Date.now() - new Date(lastTickAt).getTime()) > STALE_MS
  const exitPrice = position.direction === 'BUY' ? bid : ask
  if (stale || !exitPrice) {
    return { pnl: position.floatingPnl, pips: position.floatingPnlPips, isLive: false }
  }

  const priceDelta = position.direction === 'BUY'
    ? exitPrice - position.entry
    : position.entry - exitPrice
  const pips = Math.round(priceDelta * 10000)
  // EURUSD: 1 lot × 1 pip = $10. Contract size 100,000.
  const pnl = Math.round(priceDelta * position.lotSize * 100000 * 100) / 100
  return { pnl, pips, isLive: true }
}

export function PositionCard({ position, bid, ask, lastTickAt, onCloseMarket }: PositionCardProps) {
  const [confirming, setConfirming] = useState(false)
  const [closing, setClosing] = useState(false)

  const live = useMemo(
    () => interpolatePnl(position, bid, ask, lastTickAt),
    [position, bid, ask, lastTickAt],
  )
  const displayPnl = live.pnl
  const displayPips = live.pips

  // Mid untuk display jarak ke SL/TP (cosmetic — bid/ask sama-sama OK di sini)
  const currentPrice = bid !== undefined && ask !== undefined ? (bid + ask) / 2 : undefined

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

        {/* Entry / SL / TP — dengan distance dari current price kalau ACTIVE */}
        <div className="space-y-1 text-sm">
          <div className="flex items-center justify-between">
            <span className="text-muted-foreground">Entry</span>
            <span className="font-mono font-semibold">{position.entry.toFixed(5)}</span>
          </div>
          {position.stopLoss && position.stopLoss > 0 && (
            <div className="flex items-center justify-between">
              <span className="text-red-600 dark:text-red-400 text-xs">SL</span>
              <div className="flex items-baseline gap-1.5">
                <span className="font-mono text-xs text-red-600 dark:text-red-400">
                  {position.stopLoss.toFixed(5)}
                </span>
                <span className="text-[10px] text-muted-foreground font-mono">
                  ({Math.round(Math.abs(position.entry - position.stopLoss) * 10000)}p)
                </span>
                {position.status === 'ACTIVE' && currentPrice && (
                  <span className="text-[10px] text-muted-foreground font-mono">
                    · {Math.round(Math.abs(currentPrice - position.stopLoss) * 10000)}p away
                  </span>
                )}
              </div>
            </div>
          )}
          {position.takeProfit && position.takeProfit > 0 && (
            <div className="flex items-center justify-between">
              <span className="text-emerald-600 dark:text-emerald-400 text-xs">TP</span>
              <div className="flex items-baseline gap-1.5">
                <span className="font-mono text-xs text-emerald-600 dark:text-emerald-400">
                  {position.takeProfit.toFixed(5)}
                </span>
                <span className="text-[10px] text-muted-foreground font-mono">
                  ({Math.round(Math.abs(position.entry - position.takeProfit) * 10000)}p)
                </span>
                {position.status === 'ACTIVE' && currentPrice && (
                  <span className="text-[10px] text-muted-foreground font-mono">
                    · {Math.round(Math.abs(currentPrice - position.takeProfit) * 10000)}p away
                  </span>
                )}
              </div>
            </div>
          )}
        </div>

        {/* Floating P&L (interpolated from live MIFX price kalau ACTIVE) */}
        <div className="flex items-center justify-between">
          <span className="text-sm text-muted-foreground flex items-center gap-1">
            P&amp;L
            {live.isLive && (
              <span className="inline-block w-1.5 h-1.5 bg-emerald-500 rounded-full animate-pulse"
                title="Live: PnL diinterpolasi dari current MIFX price antara tick broker" />
            )}
          </span>
          <div className="flex items-baseline gap-1">
            <span
              className={cn(
                'font-mono font-bold text-lg',
                displayPnl >= 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400',
              )}
            >
              {displayPnl >= 0 ? '+' : ''}
              {displayPnl.toFixed(2)}
            </span>
            <span className="text-xs text-muted-foreground">
              ({displayPips >= 0 ? '+' : ''}{displayPips} pips)
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
                    displayPnl >= 0
                      ? 'bg-emerald-600 hover:bg-emerald-700'
                      : 'bg-red-600 hover:bg-red-700',
                  )}
                  onClick={handleClose}
                  disabled={closing}
                  title={`Close akan eksekusi market & catat ${displayPnl >= 0 ? 'WIN' : 'LOSS'} dari floating P&L`}
                >
                  {closing
                    ? 'Closing…'
                    : `Confirm: close as ${displayPnl >= 0 ? 'WIN' : 'LOSS'} (${displayPnl >= 0 ? '+' : ''}${displayPnl.toFixed(2)})`}
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
