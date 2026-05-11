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
          <span className="text-sm text-muted-foreground">Account health — no data</span>
        </CardContent>
      </Card>
    )
  }

  const state = deriveHealthState(data.drawdownPct)
  const color = drawdownColor(state)

  return (
    <Card className={cn(state === 'critical' || state === 'stopped' ? 'border-red-300 dark:border-red-800' : '')}>
      <CardContent className="p-4 space-y-3">
        <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
          Account Health
        </p>

        <div className="grid grid-cols-3 gap-3">
          <div>
            <p className="text-xs text-muted-foreground">Equity</p>
            <p className="font-mono font-bold text-sm">${data.equity.toLocaleString()}</p>
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
