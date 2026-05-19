'use client'

import { useEffect, useState } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { fetchAdaptiveStats } from '@/lib/api'
import type { AdaptiveStatsResponse, BucketStat } from '@/lib/types'

export default function AdaptivePage() {
  const [data, setData] = useState<AdaptiveStatsResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [window, setWindow] = useState(30)

  useEffect(() => {
    let alive = true
    const load = async () => {
      try {
        setLoading(true)
        const s = await fetchAdaptiveStats(window)
        if (alive) { setData(s); setErr(null) }
      } catch (e) {
        if (alive) setErr(e instanceof Error ? e.message : 'Unknown error')
      } finally {
        if (alive) setLoading(false)
      }
    }
    load()
    const t = setInterval(load, 30_000)  // refresh tiap 30s
    return () => { alive = false; clearInterval(t) }
  }, [window])

  if (loading && !data) {
    return <div className="p-8 text-muted-foreground">Loading adaptive stats…</div>
  }
  if (err) {
    return <div className="p-8 text-red-500">Error: {err}</div>
  }
  if (!data) return null

  const tradesNeeded = Math.max(0, 50 - data.totalTradeCount)

  return (
    <div className="p-4 sm:p-8 max-w-7xl mx-auto space-y-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-bold">Adaptive Learning — Phase 1 (Observe)</h1>
        <p className="text-sm text-muted-foreground">
          Per-bucket win rate + Wilson 95% confidence interval + expectancy.
          Auto-action (Phase 2) belum aktif — sekarang collect data dulu.
        </p>
      </div>

      {/* Global gate banner */}
      <div className={`rounded-lg border p-4 ${data.globalGateOpen ? 'border-emerald-500/40 bg-emerald-500/5' : 'border-amber-500/40 bg-amber-500/5'}`}>
        <div className="flex items-baseline justify-between">
          <div>
            <p className="text-sm font-semibold">
              {data.globalGateOpen
                ? '✅ Global gate OPEN'
                : `⏳ Global gate CLOSED — butuh ${tradesNeeded} trade lagi`}
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              {data.globalGateOpen
                ? `Total ${data.totalTradeCount} trade — Adaptive Engine bisa eligible activate (P2).`
                : `Adaptive Engine tidak akan auto-fire sampai ≥ 50 trade total. Current: ${data.totalTradeCount}/50.`}
            </p>
          </div>
          <div className="text-right text-xs font-mono text-muted-foreground">
            window: <span className="font-bold">{data.windowTradeCount}</span> / {data.windowSize}<br />
            total: <span className="font-bold">{data.totalTradeCount}</span>
          </div>
        </div>
      </div>

      {/* Overall summary */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <SummaryCard label="Win Rate (rolling)" value={`${(data.overallWinRate * 100).toFixed(1)}%`} />
        <SummaryCard label="Expectancy (R)" value={data.overallExpectancyR.toFixed(2) + 'R'} tone={data.overallExpectancyR > 0 ? 'good' : 'bad'} />
        <SummaryCard label="Avg PnL/trade" value={`$${data.overallExpectancyUsd.toFixed(2)}`} tone={data.overallExpectancyUsd > 0 ? 'good' : 'bad'} />
        <SummaryCard label="Sample" value={`${data.windowTradeCount} trades`} />
      </div>

      {/* Window size selector */}
      <div className="flex items-center gap-2 text-sm">
        <span className="text-muted-foreground">Rolling window:</span>
        {[20, 30, 50, 100, 0].map((n) => (
          <button
            key={n}
            onClick={() => setWindow(n)}
            className={`px-2 py-1 rounded text-xs font-mono ${window === n ? 'bg-primary text-primary-foreground' : 'bg-muted hover:bg-muted/80'}`}
          >
            {n === 0 ? 'All' : n}
          </button>
        ))}
      </div>

      {/* Bucket tables */}
      <BucketTable title="By Regime"           buckets={data.byRegime} />
      <BucketTable title="By Session"          buckets={data.bySession} />
      <BucketTable title="By Pattern"          buckets={data.byPattern} />
      <BucketTable title="By Zone (SMC)"       buckets={data.byZone} />
      <BucketTable title="By Confidence Band"  buckets={data.byConfidenceBand} />
      <BucketTable title="By Sweep Flag"       buckets={data.bySweepFlag} />
      <BucketTable title="By Exit Reason"      buckets={data.byExitReason} />

      <div className="text-xs text-muted-foreground space-y-1 pt-4 border-t border-border">
        <p>📊 Wilson 95% CI: range probabilitas asli win rate untuk bucket itu, given sample size.
          Wide CI = sample kecil, jangan dipakai untuk action. Narrow CI = reliable.</p>
        <p>✅ Bucket Ready (highlighted) = ≥ 20 trade. Bucket inilah yang eligible untuk Tier 1 auto-tune di P2.</p>
        <p>R = R-multiple expectancy. +0.05R/trade = positive edge baseline. R &lt; 0 = losing strategy untuk bucket itu.</p>
      </div>
    </div>
  )
}

function SummaryCard({ label, value, tone }: { label: string; value: string; tone?: 'good' | 'bad' }) {
  const toneCls = tone === 'good' ? 'text-emerald-500' : tone === 'bad' ? 'text-red-500' : ''
  return (
    <Card>
      <CardContent className="p-3">
        <p className="text-xs text-muted-foreground">{label}</p>
        <p className={`text-lg font-bold font-mono ${toneCls}`}>{value}</p>
      </CardContent>
    </Card>
  )
}

function BucketTable({ title, buckets }: { title: string; buckets: BucketStat[] }) {
  if (buckets.length === 0) {
    return (
      <Card>
        <CardHeader><CardTitle className="text-sm">{title}</CardTitle></CardHeader>
        <CardContent className="text-xs text-muted-foreground">Belum ada data.</CardContent>
      </Card>
    )
  }
  return (
    <Card>
      <CardHeader><CardTitle className="text-sm">{title}</CardTitle></CardHeader>
      <CardContent className="p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-xs font-mono">
            <thead className="bg-muted/50">
              <tr>
                <th className="text-left p-2">Label</th>
                <th className="text-right p-2">Trades</th>
                <th className="text-right p-2">WR</th>
                <th className="text-right p-2">95% CI</th>
                <th className="text-right p-2">Exp(R)</th>
                <th className="text-right p-2">Avg PnL</th>
                <th className="text-right p-2">MFE/MAE</th>
                <th className="text-center p-2">Ready</th>
              </tr>
            </thead>
            <tbody>
              {buckets.map((b) => (
                <tr key={b.label} className={`border-t border-border ${b.bucketReady ? 'bg-emerald-500/5' : ''}`}>
                  <td className="p-2 font-sans">{b.label}</td>
                  <td className="text-right p-2">{b.trades}</td>
                  <td className={`text-right p-2 font-bold ${b.winRate >= 0.5 ? 'text-emerald-500' : 'text-amber-500'}`}>
                    {(b.winRate * 100).toFixed(0)}%
                  </td>
                  <td className="text-right p-2 text-muted-foreground">
                    {(b.wilsonLower95 * 100).toFixed(0)}–{(b.wilsonUpper95 * 100).toFixed(0)}%
                  </td>
                  <td className={`text-right p-2 ${b.expectancyR > 0 ? 'text-emerald-500' : b.expectancyR < 0 ? 'text-red-500' : 'text-muted-foreground'}`}>
                    {b.expectancyR > 0 ? '+' : ''}{b.expectancyR.toFixed(2)}
                  </td>
                  <td className={`text-right p-2 ${b.avgPnlUsd > 0 ? 'text-emerald-500' : 'text-red-500'}`}>
                    {b.avgPnlUsd > 0 ? '+' : ''}${b.avgPnlUsd.toFixed(1)}
                  </td>
                  <td className="text-right p-2 text-muted-foreground">
                    {b.avgMfePips.toFixed(0)}/{b.avgMaePips.toFixed(0)}p
                  </td>
                  <td className="text-center p-2">
                    {b.bucketReady ? <span className="text-emerald-500">✓</span> : <span className="text-muted-foreground">—</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  )
}
