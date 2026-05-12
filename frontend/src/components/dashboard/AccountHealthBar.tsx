'use client'

import { ProgressBar } from '@tremor/react'
import { Card, CardContent } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import type { AccountHealthData } from '@/lib/types'

type TremorColor = 'emerald' | 'amber' | 'red'
type AccountHealthState = 'normal' | 'warning' | 'critical' | 'stopped'

function deriveHealthState(drawdownPct: number): AccountHealthState {
  if (drawdownPct >= 0.10) return 'stopped'
  if (drawdownPct >= 0.09) return 'critical'
  if (drawdownPct >= 0.07) return 'warning'
  return 'normal'
}

function drawdownColor(state: AccountHealthState): TremorColor {
  if (state === 'stopped' || state === 'critical') return 'red'
  if (state === 'warning') return 'amber'
  return 'emerald'
}

interface AccountHealthBarProps {
  data: AccountHealthData | null
}

export function AccountHealthBar({ data }: AccountHealthBarProps) {
  if (!data) {
    return (
      <Card className="opacity-50">
        <CardContent className="p-4">
          <span className="text-sm text-muted-foreground">Account health — loading...</span>
        </CardContent>
      </Card>
    )
  }

  const state = deriveHealthState(data.drawdownPct)
  const color = drawdownColor(state)
  const isLive = data.source === 'LIVE'
  // Baseline PnL: equity vs peakEquity (akurat untuk mode live maupun simulasi)
  const pnl = data.equity - data.peakEquity

  return (
    <Card className={cn(state === 'critical' || state === 'stopped' ? 'border-red-300 dark:border-red-800' : '')}>
      <CardContent className="p-4 space-y-3">
        <div className="flex items-center justify-between">
          <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
            Account Health
          </p>
          {isLive ? (
            <span className="flex items-center gap-1 text-xs font-medium text-emerald-600 dark:text-emerald-400">
              <span className="relative flex h-1.5 w-1.5">
                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75" />
                <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-emerald-500" />
              </span>
              Monex Demo
            </span>
          ) : (
            <span className="text-xs text-muted-foreground/60 italic">simulasi</span>
          )}
        </div>

        <div className="grid grid-cols-3 gap-3">
          <div>
            <p className="text-xs text-muted-foreground">Equity</p>
            <p className="font-mono font-bold text-sm">${data.equity.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</p>
            <p className={cn('text-xs font-mono', pnl >= 0 ? 'text-emerald-500' : 'text-red-500')}>
              {pnl >= 0 ? '+' : ''}{pnl.toFixed(2)}
            </p>
          </div>
          <div>
            <p className="text-xs text-muted-foreground mb-1">Drawdown</p>
            <div className="flex items-center gap-1">
              <ProgressBar value={data.drawdownPct * 100} color={color} className="flex-1" />
              <span className="text-xs font-mono">{(data.drawdownPct * 100).toFixed(1)}%</span>
            </div>
          </div>
          <div>
            <p className="text-xs text-muted-foreground">Positions</p>
            <p className="font-mono font-bold text-sm">{data.openPositions}/{data.maxPositions}</p>
          </div>
        </div>

        {(data.totalTrades !== undefined && data.totalTrades > 0) && (
          <div className="flex items-center gap-4 pt-1 border-t border-border/50 text-xs">
            <div className="flex items-center gap-1 text-muted-foreground">
              <span>Trades closed:</span>
              <span className="font-mono font-semibold text-foreground">{data.totalTrades}</span>
            </div>
            {data.winRate !== undefined && (
              <div className="flex items-center gap-1 text-muted-foreground">
                <span>Win rate:</span>
                <span className={cn('font-mono font-semibold', data.winRate >= 0.5 ? 'text-emerald-500' : 'text-red-500')}>
                  {(data.winRate * 100).toFixed(0)}%
                </span>
              </div>
            )}
          </div>
        )}

        {state === 'stopped' && (
          <div className="bg-red-100 dark:bg-red-950/30 border border-red-300 rounded-md p-2 mt-2">
            <p className="text-xs text-red-600 dark:text-red-400 font-semibold">⛔ SYSTEM STOP — 10% drawdown limit reached</p>
            <p className="text-xs text-red-500 dark:text-red-400">New trades blocked until equity recovers</p>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
