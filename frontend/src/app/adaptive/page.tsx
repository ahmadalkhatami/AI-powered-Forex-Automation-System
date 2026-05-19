'use client'

import { useEffect, useState } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { useToast } from '@/hooks/use-toast'
import {
  fetchAdaptiveStats,
  fetchAdaptiveState,
  rollbackAdaptive,
  setAdaptiveMasterDisabled,
  setAdaptiveActionDisabled,
} from '@/lib/api'
import type { AdaptiveStatsResponse, AdaptiveStateResponse, BucketStat, AdaptiveAuditEntry } from '@/lib/types'

export default function AdaptivePage() {
  const { toast } = useToast()
  const [data, setData] = useState<AdaptiveStatsResponse | null>(null)
  const [state, setState] = useState<AdaptiveStateResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [windowSize, setWindowSize] = useState(30)

  useEffect(() => {
    let alive = true
    const load = async () => {
      try {
        setLoading(true)
        const [s, st] = await Promise.all([fetchAdaptiveStats(windowSize), fetchAdaptiveState()])
        if (alive) { setData(s); setState(st); setErr(null) }
      } catch (e) {
        if (alive) setErr(e instanceof Error ? e.message : 'Unknown error')
      } finally {
        if (alive) setLoading(false)
      }
    }
    load()
    const t = setInterval(load, 30_000)  // refresh tiap 30s
    return () => { alive = false; clearInterval(t) }
  }, [windowSize])

  const handleRollback = async (snapshotId: string) => {
    if (!window.confirm(`Rollback ke snapshot ${snapshotId}? Action ini akan restore state ke before-state dari adjustment ini.`)) return
    try {
      const r = await rollbackAdaptive(snapshotId, 'manual-ui')
      toast({ title: r.success ? '✅ Rollback berhasil' : '❌ Rollback gagal', description: r.message })
      // refresh data
      const [s, st] = await Promise.all([fetchAdaptiveStats(windowSize), fetchAdaptiveState()])
      setData(s); setState(st)
    } catch (e) {
      toast({ title: 'Error', description: e instanceof Error ? e.message : 'Unknown', variant: 'destructive' })
    }
  }

  const handleToggleMaster = async () => {
    if (!state) return
    const next = !state.masterDisabled
    try {
      await setAdaptiveMasterDisabled(next)
      toast({
        title: next ? '🛑 Adaptive Engine DISABLED' : '✅ Adaptive Engine ENABLED',
        description: next ? 'Semua auto-action di-block' : 'Auto-action akan resume di cycle berikutnya',
      })
      const st = await fetchAdaptiveState()
      setState(st)
    } catch (e) {
      toast({ title: 'Toggle gagal', description: e instanceof Error ? e.message : 'Unknown', variant: 'destructive' })
    }
  }

  const handleToggleAction = async (actionName: string, currentDisabled: boolean) => {
    try {
      await setAdaptiveActionDisabled(actionName, !currentDisabled)
      const st = await fetchAdaptiveState()
      setState(st)
    } catch (e) {
      toast({ title: 'Toggle gagal', description: e instanceof Error ? e.message : 'Unknown', variant: 'destructive' })
    }
  }

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
        <h1 className="text-2xl font-bold">Adaptive Learning</h1>
        <p className="text-sm text-muted-foreground">
          Per-bucket statistics + Tier 1 auto-tune control panel.
          Engine fires every 6h saat global gate OPEN (≥50 trade closed).
        </p>
      </div>

      {/* Control panel: master + per-action toggle */}
      {state && (
        <Card>
          <CardHeader>
            <CardTitle className="text-sm flex items-center justify-between">
              <span>Engine Control</span>
              <Button
                size="sm"
                variant={state.masterDisabled ? 'default' : 'destructive'}
                onClick={handleToggleMaster}
              >
                {state.masterDisabled ? '▶ Enable Engine' : '⏸ Disable Engine'}
              </Button>
            </CardTitle>
          </CardHeader>
          <CardContent className="text-xs space-y-2">
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
              <ActionToggle name="RegimeThreshold" label="Regime Threshold (Action 1)" disabled={state.regimeThresholdActionDisabled} onToggle={() => handleToggleAction('RegimeThreshold', state.regimeThresholdActionDisabled)} />
              <ActionToggle name="SessionPenalty" label="Session Penalty/Skip (Action 2)" disabled={state.sessionPenaltyActionDisabled} onToggle={() => handleToggleAction('SessionPenalty', state.sessionPenaltyActionDisabled)} />
              <ActionToggle name="Cooldown" label="Cooldown Adapt (Action 3)" disabled={state.cooldownActionDisabled} onToggle={() => handleToggleAction('Cooldown', state.cooldownActionDisabled)} />
              <ActionToggle name="Pattern" label="Pattern Disable (Action 4)" disabled={state.patternActionDisabled} onToggle={() => handleToggleAction('Pattern', state.patternActionDisabled)} />
            </div>
            {state.masterDisabled && (
              <p className="text-amber-500 font-semibold pt-2 border-t border-border">⚠ Master kill switch ON — semua adaptive action di-block</p>
            )}
          </CardContent>
        </Card>
      )}

      {/* Active overrides snapshot */}
      {state && <ActiveOverrides state={state} /> }

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
            onClick={() => setWindowSize(n)}
            className={`px-2 py-1 rounded text-xs font-mono ${windowSize === n ? 'bg-primary text-primary-foreground' : 'bg-muted hover:bg-muted/80'}`}
          >
            {n === 0 ? 'All' : n}
          </button>
        ))}
      </div>

      {/* Audit timeline */}
      {state && state.auditHistory.length > 0 && (
        <AuditTimeline entries={state.auditHistory} onRollback={handleRollback} />
      )}

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

function ActionToggle({ label, disabled, onToggle }: { name: string; label: string; disabled: boolean; onToggle: () => void }) {
  return (
    <button
      onClick={onToggle}
      className={`text-left p-2 rounded border transition-colors ${disabled ? 'border-amber-500/40 bg-amber-500/5 hover:bg-amber-500/10' : 'border-emerald-500/40 bg-emerald-500/5 hover:bg-emerald-500/10'}`}
    >
      <div className="flex items-center gap-2">
        <span className={disabled ? 'text-amber-500' : 'text-emerald-500'}>
          {disabled ? '⏸' : '▶'}
        </span>
        <span className="font-mono text-xs">{disabled ? 'OFF' : 'ON'}</span>
      </div>
      <p className="text-[10px] text-muted-foreground mt-1">{label}</p>
    </button>
  )
}

function ActiveOverrides({ state }: { state: AdaptiveStateResponse }) {
  const regimeEntries = Object.entries(state.regimeThresholdOverride)
  const sessionPenEntries = Object.entries(state.sessionPenalty)
  const sessionSkipEntries = Object.entries(state.sessionSkipUntil)
    .filter(([, until]) => new Date(until).getTime() > Date.now())
  const cooldownEntries = Object.entries(state.cooldownOverride)
  const patternDisEntries = Object.entries(state.patternDisableUntil)
    .filter(([, until]) => new Date(until).getTime() > Date.now())

  const total = regimeEntries.length + sessionPenEntries.length + sessionSkipEntries.length
              + cooldownEntries.length + patternDisEntries.length

  if (total === 0) {
    return (
      <Card>
        <CardHeader><CardTitle className="text-sm">Active Overrides</CardTitle></CardHeader>
        <CardContent className="text-xs text-muted-foreground">
          Belum ada override aktif. Adaptive Engine akan fire saat global gate OPEN + bucket reach ready threshold.
        </CardContent>
      </Card>
    )
  }

  return (
    <Card>
      <CardHeader><CardTitle className="text-sm">Active Overrides ({total})</CardTitle></CardHeader>
      <CardContent className="text-xs space-y-3">
        {regimeEntries.length > 0 && (
          <OverrideRow label="Regime Threshold" entries={regimeEntries.map(([k,v]) => ({ key: k, value: `${(v * 100).toFixed(0)}%` }))} />
        )}
        {sessionPenEntries.length > 0 && (
          <OverrideRow label="Session Penalty" entries={sessionPenEntries.map(([k,v]) => ({ key: k, value: `-${(v * 100).toFixed(0)}%` }))} />
        )}
        {sessionSkipEntries.length > 0 && (
          <OverrideRow label="Session SKIP" entries={sessionSkipEntries.map(([k,v]) => ({ key: k, value: `until ${new Date(v).toLocaleDateString()}` }))} tone="warn" />
        )}
        {cooldownEntries.length > 0 && (
          <OverrideRow label="Cooldown" entries={cooldownEntries.map(([k,v]) => ({ key: k, value: `${v} min` }))} />
        )}
        {patternDisEntries.length > 0 && (
          <OverrideRow label="Pattern Disabled" entries={patternDisEntries.map(([k,v]) => ({ key: k, value: `until ${new Date(v).toLocaleDateString()}` }))} tone="warn" />
        )}
      </CardContent>
    </Card>
  )
}

function OverrideRow({ label, entries, tone }: { label: string; entries: { key: string; value: string }[]; tone?: 'warn' }) {
  return (
    <div className="flex items-baseline gap-2 flex-wrap">
      <span className="text-muted-foreground min-w-[120px]">{label}:</span>
      {entries.map((e) => (
        <span key={e.key} className={`px-2 py-0.5 rounded font-mono ${tone === 'warn' ? 'bg-amber-500/10 text-amber-500' : 'bg-muted'}`}>
          {e.key} <span className="text-muted-foreground">→</span> {e.value}
        </span>
      ))}
    </div>
  )
}

function AuditTimeline({ entries, onRollback }: { entries: AdaptiveAuditEntry[]; onRollback: (snapshotId: string) => void }) {
  return (
    <Card>
      <CardHeader><CardTitle className="text-sm">Audit History ({entries.length})</CardTitle></CardHeader>
      <CardContent className="p-0">
        <div className="divide-y divide-border">
          {entries.map((e, i) => {
            const ts = new Date(e.timestamp)
            const actionColor =
              e.action === 'Revert' ? 'text-amber-500' :
              e.action === 'PatternDisable' || e.action === 'SessionSkip' ? 'text-red-500' :
              'text-emerald-500'
            return (
              <div key={`${e.snapshotId}-${i}`} className="p-3 text-xs space-y-1">
                <div className="flex items-center justify-between gap-2">
                  <div className="flex items-baseline gap-2">
                    <span className="text-muted-foreground font-mono text-[10px]">
                      {ts.toISOString().slice(0, 19).replace('T', ' ')}
                    </span>
                    <span className={`font-semibold ${actionColor}`}>{e.action}</span>
                    <span className="font-mono">[{e.bucket}]</span>
                    <span className="text-muted-foreground">
                      {e.fromValue} <span className="text-foreground">→</span> {e.toValue}
                    </span>
                  </div>
                  {e.action !== 'Revert' && e.snapshotId && (
                    <Button
                      size="sm"
                      variant="ghost"
                      className="h-6 text-xs"
                      onClick={() => onRollback(e.snapshotId)}
                    >
                      ↶ Rollback
                    </Button>
                  )}
                </div>
                <p className="text-muted-foreground">{e.reason}</p>
                {(e.sampleSize > 0 || e.expectancyR != null) && (
                  <p className="text-[10px] text-muted-foreground font-mono">
                    {e.sampleSize > 0 && `n=${e.sampleSize}`}
                    {e.wilsonLower != null && e.wilsonUpper != null && ` · Wilson 95%: ${(e.wilsonLower*100).toFixed(0)}–${(e.wilsonUpper*100).toFixed(0)}%`}
                    {e.expectancyR != null && ` · ExpR=${e.expectancyR.toFixed(2)}`}
                  </p>
                )}
              </div>
            )
          })}
        </div>
      </CardContent>
    </Card>
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
