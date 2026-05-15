'use client'

import { useEffect, useRef, useState } from 'react'
import { Play } from 'lucide-react'
import {
  createChart,
  ColorType,
  AreaSeries,
  type IChartApi,
  type ISeriesApi,
  type UTCTimestamp,
} from 'lightweight-charts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { runBacktest } from '@/lib/api'
import type { BacktestResult, ChartTimeframe } from '@/lib/types'

const TIMEFRAMES: ChartTimeframe[] = ['M15', 'H1', 'D1']

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

function EquityChart({ result }: { result: BacktestResult }) {
  const containerRef = useRef<HTMLDivElement>(null)
  const chartRef = useRef<IChartApi | null>(null)
  const seriesRef = useRef<ISeriesApi<'Area'> | null>(null)

  useEffect(() => {
    if (!containerRef.current) return
    const chart = createChart(containerRef.current, {
      layout: {
        background: { type: ColorType.Solid, color: 'transparent' },
        textColor: '#9ca3af',
        fontSize: 10,
      },
      grid: {
        vertLines: { color: 'rgba(255,255,255,0.04)' },
        horzLines: { color: 'rgba(255,255,255,0.04)' },
      },
      rightPriceScale: { borderColor: 'rgba(255,255,255,0.08)' },
      timeScale: { borderColor: 'rgba(255,255,255,0.08)', timeVisible: true },
      width: containerRef.current.clientWidth,
      height: 240,
    })
    const series = chart.addSeries(AreaSeries, {
      lineColor: '#22c55e',
      topColor: 'rgba(34,197,94,0.3)',
      bottomColor: 'rgba(34,197,94,0.0)',
      lineWidth: 2,
    })
    chartRef.current = chart
    seriesRef.current = series
    const ro = new ResizeObserver(() => {
      if (containerRef.current) chart.applyOptions({ width: containerRef.current.clientWidth })
    })
    ro.observe(containerRef.current)
    return () => {
      ro.disconnect()
      chart.remove()
      chartRef.current = null
      seriesRef.current = null
    }
  }, [])

  useEffect(() => {
    if (!seriesRef.current) return
    const isUp = result.netPnl >= 0
    const color = isUp ? '#22c55e' : '#ef4444'
    seriesRef.current.applyOptions({
      lineColor: color,
      topColor: isUp ? 'rgba(34,197,94,0.3)' : 'rgba(239,68,68,0.3)',
      bottomColor: isUp ? 'rgba(34,197,94,0.0)' : 'rgba(239,68,68,0.0)',
    })
    // Dedup waktu (strict ascending) jika ada duplicate
    const seen = new Set<number>()
    const data = result.equityCurve
      .filter((p) => {
        if (seen.has(p.time)) return false
        seen.add(p.time)
        return true
      })
      .map((p) => ({ time: p.time as UTCTimestamp, value: p.equity }))
    seriesRef.current.setData(data)
    chartRef.current?.timeScale().fitContent()
  }, [result])

  return <div ref={containerRef} className="w-full" />
}

export default function BacktestPage() {
  const [pair] = useState('EURUSD')
  const [timeframe, setTimeframe] = useState<ChartTimeframe>('M15')
  const [startingEquity, setStartingEquity] = useState(1000)
  const [minConfidence, setMinConfidence] = useState(0.6)
  const [minConfluence, setMinConfluence] = useState(70)
  const [maxBarsPerTrade, setMaxBarsPerTrade] = useState(96)
  const [blockHold, setBlockHold] = useState(true)
  const [loading, setLoading] = useState(false)
  const [result, setResult] = useState<BacktestResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const handleRun = async () => {
    setLoading(true)
    setError(null)
    try {
      const r = await runBacktest({
        pair,
        timeframe,
        startingEquity,
        maxBarsPerTrade,
        minConfidence,
        minConfluence,
        blockHold,
      })
      setResult(r)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error')
      setResult(null)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="space-y-4 max-w-5xl mx-auto">
      <h1 className="text-xl font-semibold">Backtest Harness</h1>
      <p className="text-xs text-muted-foreground">
        Replay candle history (cache MIFX EA) lewat pipeline yang sama dengan live
        (LiveSignalAnalyzer + ATR-based SL/TP). Simulate fill di next-bar open, scan SL/TP hit
        sampai timeout. Strategi conservative: kalau SL & TP keduanya tersentuh di bar sama → LOSS.
      </p>

      <Card>
        <CardHeader className="pb-2 pt-3 px-4">
          <CardTitle className="text-sm">Parameter</CardTitle>
        </CardHeader>
        <CardContent className="p-4 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
          <div className="space-y-1">
            <label className="text-[10px] uppercase tracking-wider text-muted-foreground">Pair</label>
            <input
              value={pair}
              disabled
              className="w-full px-2 py-1.5 text-xs font-mono rounded border border-border/40 bg-muted/30"
            />
          </div>
          <div className="space-y-1">
            <label className="text-[10px] uppercase tracking-wider text-muted-foreground">Timeframe</label>
            <div className="flex gap-1">
              {TIMEFRAMES.map((tf) => (
                <button
                  key={tf}
                  onClick={() => setTimeframe(tf)}
                  className={cn(
                    'flex-1 px-2 py-1.5 text-xs font-mono rounded border transition-colors',
                    tf === timeframe
                      ? 'bg-primary/15 border-primary/40 text-primary'
                      : 'border-border/40 text-muted-foreground hover:text-foreground hover:border-border',
                  )}
                >
                  {tf}
                </button>
              ))}
            </div>
          </div>
          <div className="space-y-1">
            <label className="text-[10px] uppercase tracking-wider text-muted-foreground">Starting Equity</label>
            <input
              type="number"
              value={startingEquity}
              onChange={(e) => setStartingEquity(Number(e.target.value) || 0)}
              className="w-full px-2 py-1.5 text-xs font-mono rounded border border-border/40 bg-background"
            />
          </div>
          <div className="space-y-1">
            <label className="text-[10px] uppercase tracking-wider text-muted-foreground">
              Min Confidence (konsensus): {(minConfidence * 100).toFixed(0)}%
            </label>
            <input
              type="range"
              min="0"
              max="0.95"
              step="0.05"
              value={minConfidence}
              onChange={(e) => setMinConfidence(Number(e.target.value))}
              className="w-full"
            />
            <p className="text-[10px] text-muted-foreground/70 italic">
              Seberapa setuju indikator (low stddev). 60% default.
            </p>
          </div>
          <div className="space-y-1">
            <label className="text-[10px] uppercase tracking-wider text-muted-foreground">
              Min Confluence (kualitas): {minConfluence}
            </label>
            <input
              type="range"
              min="0"
              max="100"
              step="5"
              value={minConfluence}
              onChange={(e) => setMinConfluence(Number(e.target.value))}
              className="w-full"
            />
            <p className="text-[10px] text-muted-foreground/70 italic">
              Kualitas weighted score (trend×0.4 + momentum×0.35 + struct×0.25). 70 = setup kuat.
            </p>
          </div>
          <div className="space-y-1">
            <label className="text-[10px] uppercase tracking-wider text-muted-foreground">Max Bars per Trade</label>
            <input
              type="number"
              value={maxBarsPerTrade}
              onChange={(e) => setMaxBarsPerTrade(Number(e.target.value) || 96)}
              className="w-full px-2 py-1.5 text-xs font-mono rounded border border-border/40 bg-background"
            />
          </div>
          <div className="space-y-1 sm:col-span-3 flex sm:items-end">
            <label className="flex items-center gap-2 text-xs cursor-pointer">
              <input
                type="checkbox"
                checked={blockHold}
                onChange={(e) => setBlockHold(e.target.checked)}
              />
              Block HOLD signals (recommended)
            </label>
          </div>
        </CardContent>
        <div className="px-4 pb-4">
          <Button onClick={handleRun} disabled={loading} className="gap-2">
            <Play className={cn('h-3.5 w-3.5', loading && 'animate-pulse')} />
            {loading ? 'Running…' : 'Run Backtest'}
          </Button>
        </div>
      </Card>

      {error && (
        <div className="rounded-lg border border-red-500/40 bg-red-500/10 p-3 text-xs text-red-600 dark:text-red-400">
          {error}
        </div>
      )}

      {result && (
        <>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <StatCard
              label="Net P&L"
              value={`$${result.netPnl.toFixed(2)}`}
              tone={result.netPnl >= 0 ? 'good' : 'bad'}
              sub={`${result.finalEquity.toFixed(2)} from ${result.startingEquity}`}
            />
            <StatCard
              label="Profit Factor"
              value={result.profitFactor >= 999 ? '∞' : result.profitFactor.toFixed(2)}
              sub={
                result.profitFactor >= 1.5
                  ? 'sehat'
                  : result.profitFactor >= 1.0
                    ? 'marjinal'
                    : 'rugi'
              }
              tone={result.profitFactor >= 1.5 ? 'good' : result.profitFactor >= 1.0 ? 'neutral' : 'bad'}
            />
            <StatCard
              label="Win Rate"
              value={`${(result.winRate * 100).toFixed(1)}%`}
              sub={`${result.wins}W / ${result.losses}L / ${result.timeouts}T`}
              tone={result.winRate >= 0.5 ? 'good' : 'bad'}
            />
            <StatCard
              label="Max Drawdown"
              value={`${(result.maxDrawdownPct * 100).toFixed(1)}%`}
              sub={`${result.totalTrades} trades`}
              tone={result.maxDrawdownPct < 0.1 ? 'good' : result.maxDrawdownPct < 0.2 ? 'neutral' : 'bad'}
            />
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <StatCard label="Expectancy/trade" value={`$${result.expectancy.toFixed(2)}`} tone={result.expectancy >= 0 ? 'good' : 'bad'} />
            <StatCard label="Gross Win" value={`$${result.grossWin.toFixed(2)}`} tone="good" />
            <StatCard label="Gross Loss" value={`$${result.grossLoss.toFixed(2)}`} tone="bad" />
            <StatCard
              label="Max Loss Streak"
              value={`${result.maxConsecutiveLosses}`}
              sub={`Win streak: ${result.maxConsecutiveWins}`}
              tone="bad"
            />
          </div>

          <Card>
            <CardHeader className="pb-2 pt-3 px-4">
              <CardTitle className="text-sm">Equity Curve</CardTitle>
            </CardHeader>
            <CardContent className="px-4 pb-4">
              <EquityChart result={result} />
              <p className="text-[10px] text-muted-foreground mt-2 font-mono">
                Bars: {result.backtestBars} dari {result.candleCount} candle · {result.totalTrades} trades simulated
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-2 pt-3 px-4">
              <CardTitle className="text-sm">Trades ({result.trades.length})</CardTitle>
            </CardHeader>
            <CardContent className="p-0">
              {result.trades.length === 0 ? (
                <p className="p-4 text-xs text-muted-foreground">No trades generated dengan parameter ini.</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-xs">
                    <thead className="text-[10px] text-muted-foreground uppercase tracking-wider border-b border-border/50">
                      <tr>
                        <th className="text-left px-3 py-2">#</th>
                        <th className="text-left px-3 py-2">Entry</th>
                        <th className="text-left px-3 py-2">Dir</th>
                        <th className="text-right px-3 py-2">Price</th>
                        <th className="text-right px-3 py-2">SL</th>
                        <th className="text-right px-3 py-2">TP</th>
                        <th className="text-right px-3 py-2">Lot</th>
                        <th className="text-right px-3 py-2">Conf</th>
                        <th className="text-center px-3 py-2">Status</th>
                        <th className="text-right px-3 py-2">P&L</th>
                        <th className="text-right px-3 py-2">Pips</th>
                        <th className="text-right px-3 py-2">Bars</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border/30 font-mono">
                      {result.trades.map((t, idx) => (
                        <tr key={idx} className="hover:bg-muted/30">
                          <td className="px-3 py-1.5">{idx + 1}</td>
                          <td className="px-3 py-1.5">
                            {new Date(t.entryTime * 1000).toLocaleString('id-ID', {
                              day: '2-digit',
                              month: 'short',
                              hour: '2-digit',
                              minute: '2-digit',
                            })}
                          </td>
                          <td className={cn('px-3 py-1.5 font-semibold', t.direction === 'BUY' ? 'text-blue-500' : 'text-orange-500')}>
                            {t.direction}
                          </td>
                          <td className="text-right px-3 py-1.5">{t.entryPrice.toFixed(5)}</td>
                          <td className="text-right px-3 py-1.5 text-red-500/80">{t.stopLoss.toFixed(5)}</td>
                          <td className="text-right px-3 py-1.5 text-emerald-500/80">{t.takeProfit.toFixed(5)}</td>
                          <td className="text-right px-3 py-1.5">{t.lotSize.toFixed(2)}</td>
                          <td className="text-right px-3 py-1.5">{(t.confidence * 100).toFixed(0)}%</td>
                          <td className="text-center px-3 py-1.5">
                            <span
                              className={cn(
                                'px-1.5 py-0.5 rounded text-[10px] font-bold',
                                t.status === 'WIN' && 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400',
                                t.status === 'LOSS' && 'bg-red-500/15 text-red-600 dark:text-red-400',
                                t.status === 'TIMEOUT' && 'bg-amber-500/15 text-amber-600 dark:text-amber-400',
                              )}
                            >
                              {t.status}
                            </span>
                          </td>
                          <td className={cn('text-right px-3 py-1.5 font-bold', t.pnl >= 0 ? 'text-emerald-500' : 'text-red-500')}>
                            {t.pnl >= 0 ? '+' : ''}${t.pnl.toFixed(2)}
                          </td>
                          <td className={cn('text-right px-3 py-1.5', t.pips >= 0 ? 'text-emerald-500' : 'text-red-500')}>
                            {t.pips >= 0 ? '+' : ''}{t.pips}
                          </td>
                          <td className="text-right px-3 py-1.5">{t.barsHeld}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </CardContent>
          </Card>
        </>
      )}
    </div>
  )
}
