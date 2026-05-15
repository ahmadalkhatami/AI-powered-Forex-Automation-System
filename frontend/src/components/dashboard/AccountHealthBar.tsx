'use client'

import { ProgressBar } from '@tremor/react'
import { Card, CardContent } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { EquityCurve } from '@/components/dashboard/EquityCurve'
import type { AccountHealthData, TradePositionResponse } from '@/lib/types'

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

function dailyCapColor(utilization: number): TremorColor {
  if (utilization >= 1.0) return 'red'
  if (utilization >= 0.8) return 'amber'
  return 'emerald'
}

const TIER_LABEL: Record<string, string> = {
  starter: '$0-100 · 2%/trade · 6%/day · max 3',
  growth:  '$100-200 · 1.5%/trade · 6%/day · max 4',
  stable:  '$200-500 · 1%/trade · 5%/day · max 5',
  scaled:  '>$500 · 1%/trade · 4%/day · max 5',
}

interface AccountHealthBarProps {
  data: AccountHealthData | null
  positions?: TradePositionResponse[]
  baselineEquity?: number
}

export function AccountHealthBar({ data, positions = [], baselineEquity = 1000 }: AccountHealthBarProps) {
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

  // Daily risk cap (Sprint 1 item 1)
  const tier            = data.riskTier ?? 'starter'
  const dailyUtil       = data.dailyCapUtilization ?? 0
  const dailyCapPct     = data.dailyCapPct ?? 0.06
  const dailyCapUsd     = data.equity * dailyCapPct
  const dailyUsedUsd    = data.dailyRiskUsedUsd ?? 0
  const tradesToday     = data.tradesOpenedToday ?? 0
  const maxDailyTrades  = data.maxDailyTrades ?? 3
  const dailyCapHit     = dailyUtil >= 1.0
  const tradesCapHit    = tradesToday >= maxDailyTrades

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

        {/* Equity curve sparkline — visualisasi cumulative P&L sejak baseline */}
        <div className="pt-2 border-t border-border/50">
          <p className="text-[10px] uppercase tracking-wider text-muted-foreground/70 mb-1">
            Equity curve · sejak ${baselineEquity}
          </p>
          <EquityCurve
            positions={positions}
            baseline={baselineEquity}
            currentEquity={data.equity}
          />
        </div>

        {/* Daily risk cap (Sprint 1 item 1) */}
        <div className="pt-2 border-t border-border/50 space-y-1.5">
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Daily Risk
              </span>
              <span className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold bg-muted text-foreground/70 uppercase">
                {tier}
              </span>
            </div>
            <span className="text-[10px] text-muted-foreground/70 font-mono hidden sm:inline">
              {TIER_LABEL[tier]}
            </span>
          </div>

          <div className="flex items-center gap-2">
            <ProgressBar
              value={Math.min(dailyUtil * 100, 100)}
              color={dailyCapColor(dailyUtil)}
              className="flex-1"
            />
            <span className="text-xs font-mono whitespace-nowrap">
              ${dailyUsedUsd.toFixed(2)} / ${dailyCapUsd.toFixed(2)}
            </span>
          </div>

          <div className="flex items-center justify-between text-[10px] text-muted-foreground font-mono">
            <span>
              {(dailyUtil * 100).toFixed(0)}% used · {tradesToday}/{maxDailyTrades} trades hari ini
            </span>
            <span className="text-muted-foreground/60">resets UTC midnight</span>
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

        {(dailyCapHit || tradesCapHit) && state !== 'stopped' && (
          <div className="bg-amber-100 dark:bg-amber-950/30 border border-amber-300 rounded-md p-2 mt-2">
            <p className="text-xs text-amber-700 dark:text-amber-400 font-semibold">
              ⏸ DAILY CAP HIT — {dailyCapHit ? 'risk budget habis' : 'max trade hari ini'}
            </p>
            <p className="text-xs text-amber-600 dark:text-amber-500">
              Trade baru auto-block sampai UTC midnight (07:00 WIB)
            </p>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
