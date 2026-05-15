'use client'

import { useEffect, useMemo, useState } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { getAllPositions } from '@/lib/api'
import { EquityCurve } from '@/components/dashboard/EquityCurve'
import type { TradePositionResponse } from '@/lib/types'

interface Stats {
  total: number
  wins: number
  losses: number
  winRate: number
  grossWin: number
  grossLoss: number
  netPnl: number
  profitFactor: number
  expectancy: number
  avgWin: number
  avgLoss: number
  maxConsecutiveWins: number
  maxConsecutiveLosses: number
  avgHoldingMinutes: number
  bestTrade: TradePositionResponse | null
  worstTrade: TradePositionResponse | null
}

function deriveStats(positions: TradePositionResponse[]): Stats {
  const closed = positions.filter((p) => p.status === 'CLOSED_WIN' || p.status === 'CLOSED_LOSS')
  const wins   = closed.filter((p) => p.status === 'CLOSED_WIN')
  const losses = closed.filter((p) => p.status === 'CLOSED_LOSS')

  const grossWin  = wins.reduce((s, p) => s + Math.abs(p.floatingPnl), 0)
  const grossLoss = losses.reduce((s, p) => s + Math.abs(p.floatingPnl), 0)
  const netPnl    = grossWin - grossLoss

  const profitFactor = grossLoss > 0 ? grossWin / grossLoss : (grossWin > 0 ? Infinity : 0)
  const expectancy   = closed.length > 0 ? netPnl / closed.length : 0
  const avgWin       = wins.length > 0   ? grossWin  / wins.length   : 0
  const avgLoss      = losses.length > 0 ? grossLoss / losses.length : 0

  // Consecutive runs (chronological order, oldest first)
  const chronological = closed
    .filter((p) => p.closedAt !== null)
    .sort((a, b) => new Date(a.closedAt!).getTime() - new Date(b.closedAt!).getTime())
  let curWin = 0, curLoss = 0, maxWin = 0, maxLoss = 0
  for (const p of chronological) {
    if (p.status === 'CLOSED_WIN') {
      curWin++; curLoss = 0
      if (curWin > maxWin) maxWin = curWin
    } else {
      curLoss++; curWin = 0
      if (curLoss > maxLoss) maxLoss = curLoss
    }
  }

  // Holding time
  const withTimes = closed.filter((p) => p.openedAt && p.closedAt)
  const totalMin = withTimes.reduce((s, p) => {
    const ms = new Date(p.closedAt!).getTime() - new Date(p.openedAt!).getTime()
    return s + ms / 60_000
  }, 0)
  const avgHoldingMinutes = withTimes.length > 0 ? totalMin / withTimes.length : 0

  const bestTrade  = closed.length > 0 ? closed.reduce((a, b) => (a.floatingPnl > b.floatingPnl ? a : b)) : null
  const worstTrade = closed.length > 0 ? closed.reduce((a, b) => (a.floatingPnl < b.floatingPnl ? a : b)) : null

  return {
    total: closed.length,
    wins: wins.length,
    losses: losses.length,
    winRate: closed.length > 0 ? wins.length / closed.length : 0,
    grossWin,
    grossLoss,
    netPnl,
    profitFactor,
    expectancy,
    avgWin,
    avgLoss,
    maxConsecutiveWins: maxWin,
    maxConsecutiveLosses: maxLoss,
    avgHoldingMinutes,
    bestTrade,
    worstTrade,
  }
}

function StatCard({
  label,
  value,
  sub,
  tone,
}: {
  label: string
  value: string
  sub?: string
  tone?: 'good' | 'bad' | 'neutral'
}) {
  return (
    <Card>
      <CardContent className="p-3 space-y-0.5">
        <p className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</p>
        <p
          className={cn(
            'font-mono font-bold text-base',
            tone === 'good' && 'text-emerald-500',
            tone === 'bad' && 'text-red-500',
          )}
        >
          {value}
        </p>
        {sub && <p className="text-[10px] text-muted-foreground font-mono">{sub}</p>}
      </CardContent>
    </Card>
  )
}

export default function AnalyticsPage() {
  const [positions, setPositions] = useState<TradePositionResponse[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    const load = async () => {
      try {
        const data = await getAllPositions()
        if (!cancelled) setPositions(data)
      } catch {
        if (!cancelled) setPositions([])
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    load()
    const id = setInterval(load, 5000)
    return () => { cancelled = true; clearInterval(id) }
  }, [])

  const stats = useMemo(() => deriveStats(positions), [positions])

  return (
    <div className="space-y-4 max-w-5xl mx-auto">
      <h1 className="text-xl font-semibold">Performance Analytics</h1>

      {loading && positions.length === 0 ? (
        <div className="text-sm text-muted-foreground">Loading…</div>
      ) : stats.total === 0 ? (
        <div className="text-sm text-muted-foreground">Belum ada trade closed.</div>
      ) : (
        <>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <StatCard label="Net P&L" value={`$${stats.netPnl.toFixed(2)}`} tone={stats.netPnl >= 0 ? 'good' : 'bad'} />
            <StatCard
              label="Profit Factor"
              value={stats.profitFactor === Infinity ? '∞' : stats.profitFactor.toFixed(2)}
              sub={stats.profitFactor >= 1.5 ? 'sehat' : stats.profitFactor >= 1.0 ? 'marjinal' : 'rugi'}
              tone={stats.profitFactor >= 1.5 ? 'good' : stats.profitFactor >= 1.0 ? 'neutral' : 'bad'}
            />
            <StatCard
              label="Expectancy/trade"
              value={`$${stats.expectancy.toFixed(2)}`}
              tone={stats.expectancy >= 0 ? 'good' : 'bad'}
            />
            <StatCard
              label="Win Rate"
              value={`${(stats.winRate * 100).toFixed(1)}%`}
              sub={`${stats.wins}W / ${stats.losses}L`}
              tone={stats.winRate >= 0.5 ? 'good' : 'bad'}
            />
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <StatCard label="Avg Win" value={`$${stats.avgWin.toFixed(2)}`} tone="good" />
            <StatCard label="Avg Loss" value={`$${stats.avgLoss.toFixed(2)}`} tone="bad" />
            <StatCard
              label="Max Win Streak"
              value={`${stats.maxConsecutiveWins}`}
              sub="berturut-turut"
              tone="good"
            />
            <StatCard
              label="Max Loss Streak"
              value={`${stats.maxConsecutiveLosses}`}
              sub="berturut-turut"
              tone="bad"
            />
          </div>

          <Card>
            <CardHeader className="pb-2 pt-3 px-4">
              <CardTitle className="text-sm">Equity Curve</CardTitle>
            </CardHeader>
            <CardContent className="px-4 pb-4">
              <EquityCurve
                positions={positions}
                baseline={1000}
                currentEquity={1000 + stats.netPnl}
                height={200}
              />
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-2 pt-3 px-4">
              <CardTitle className="text-sm">Extremes</CardTitle>
            </CardHeader>
            <CardContent className="px-4 pb-4 grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
              {stats.bestTrade && (
                <div className="rounded border border-emerald-500/30 bg-emerald-500/5 p-3 space-y-1">
                  <p className="font-semibold text-emerald-600 dark:text-emerald-400">🏆 Best trade</p>
                  <p className="font-mono">
                    {stats.bestTrade.direction} {stats.bestTrade.pair} ·{' '}
                    <span className="font-bold">+${stats.bestTrade.floatingPnl.toFixed(2)}</span>{' '}
                    ({stats.bestTrade.floatingPnlPips}p)
                  </p>
                  <p className="text-muted-foreground">
                    {new Date(stats.bestTrade.closedAt!).toLocaleString('id-ID')}
                  </p>
                </div>
              )}
              {stats.worstTrade && (
                <div className="rounded border border-red-500/30 bg-red-500/5 p-3 space-y-1">
                  <p className="font-semibold text-red-600 dark:text-red-400">💀 Worst trade</p>
                  <p className="font-mono">
                    {stats.worstTrade.direction} {stats.worstTrade.pair} ·{' '}
                    <span className="font-bold">${stats.worstTrade.floatingPnl.toFixed(2)}</span>{' '}
                    ({stats.worstTrade.floatingPnlPips}p)
                  </p>
                  <p className="text-muted-foreground">
                    {new Date(stats.worstTrade.closedAt!).toLocaleString('id-ID')}
                  </p>
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-2 pt-3 px-4">
              <CardTitle className="text-sm">Detail</CardTitle>
            </CardHeader>
            <CardContent className="px-4 pb-4 text-xs space-y-1 font-mono">
              <p>Total closed: <span className="font-bold">{stats.total}</span></p>
              <p>Gross win: <span className="text-emerald-500 font-bold">${stats.grossWin.toFixed(2)}</span></p>
              <p>Gross loss: <span className="text-red-500 font-bold">${stats.grossLoss.toFixed(2)}</span></p>
              <p>Avg holding time: <span className="font-bold">{stats.avgHoldingMinutes.toFixed(0)} menit</span></p>
            </CardContent>
          </Card>
        </>
      )}
    </div>
  )
}
