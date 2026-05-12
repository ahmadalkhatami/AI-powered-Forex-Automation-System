'use client'

import { ProgressBar } from '@tremor/react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { AlertTriangle } from 'lucide-react'
import type { SignalHeroData } from '@/lib/types'

type HeroState = 'active-go' | 'active-nogo' | 'active-caution' | 'no-signal' | 'monitoring'

function deriveState(
  signal: SignalHeroData | null,
  mode: 'monitoring' | 'default'
): HeroState {
  // Kalau tidak ada signal, baru tampilkan monitoring placeholder
  if (!signal) return mode === 'monitoring' ? 'monitoring' : 'no-signal'
  if (signal.decision === 'NO-GO') return 'active-nogo'
  if (signal.decision === 'GO_WITH_CAUTION') return 'active-caution'
  return 'active-go'
}

function formatTimestamp(iso: string): string {
  return new Intl.DateTimeFormat('id-ID', {
    dateStyle: 'short',
    timeStyle: 'short',
    timeZone: 'Asia/Jakarta',
  }).format(new Date(iso))
}

interface SignalHeroProps {
  signal: SignalHeroData | null
  mode: 'monitoring' | 'default'
  onTriggerAnalysis?: () => void
}

export function SignalHero({ signal, mode, onTriggerAnalysis }: SignalHeroProps) {
  const state = deriveState(signal, mode)

  return (
    <div
      role="status"
      aria-live="polite"
      className="rounded-lg border border-border bg-card p-6"
    >
      {state === 'active-go' && signal && (
        <div className="flex flex-col gap-2">
          <div className="flex items-center gap-6">
            <span className="text-5xl font-black text-emerald-600 dark:text-emerald-400">{signal.signal}</span>
            <div className="flex items-center gap-2">
              <span className="text-2xl font-bold text-emerald-600 dark:text-emerald-400">GO ✓</span>
              {mode === 'monitoring' && (
                <span className="text-xs bg-emerald-500/20 text-emerald-600 dark:text-emerald-400 border border-emerald-500/30 px-2 py-0.5 rounded-full font-semibold animate-pulse">
                  ● LIVE
                </span>
              )}
            </div>
          </div>
          <div className="flex items-center gap-3 mt-2">
            <span className="text-sm text-muted-foreground">{signal.pair} · {signal.timeframe}</span>
            <ProgressBar value={signal.confidenceScore * 100} color="emerald" className="w-32" />
            <span className="text-sm font-mono">{(signal.confidenceScore * 100).toFixed(0)}%</span>
          </div>
          <span className="text-xs text-muted-foreground">{formatTimestamp(signal.timestamp)}</span>
        </div>
      )}

      {state === 'active-nogo' && signal && (
        <div className="flex flex-col gap-2">
          <div className="flex items-center gap-6">
            <span className="text-5xl font-black text-red-600 dark:text-red-400">{signal.signal}</span>
            <span className="text-2xl font-bold text-red-600 dark:text-red-400">NO-GO ✗</span>
          </div>
          <div className="flex items-center gap-3 mt-2">
            <span className="text-sm text-muted-foreground">{signal.pair} · {signal.timeframe}</span>
            <ProgressBar value={signal.confidenceScore * 100} color="red" className="w-32" />
            <span className="text-sm font-mono">{(signal.confidenceScore * 100).toFixed(0)}%</span>
          </div>
          {signal.blockReason && (
            <Badge variant="destructive" className="mt-2 w-fit">{signal.blockReason}</Badge>
          )}
          <span className="text-xs text-muted-foreground">{formatTimestamp(signal.timestamp)}</span>
        </div>
      )}

      {state === 'active-caution' && signal && (
        <div className="flex flex-col gap-2">
          <div className="flex items-center gap-6">
            <span className="text-5xl font-black text-amber-500 dark:text-amber-400">{signal.signal}</span>
            <div className="flex items-center gap-2">
              <AlertTriangle size={20} className="text-amber-500 dark:text-amber-400" />
              <span className="text-2xl font-bold text-amber-500 dark:text-amber-400">GO WITH CAUTION</span>
              {mode === 'monitoring' && (
                <span className="text-xs bg-emerald-500/20 text-emerald-600 dark:text-emerald-400 border border-emerald-500/30 px-2 py-0.5 rounded-full font-semibold animate-pulse">
                  ● LIVE
                </span>
              )}
            </div>
          </div>
          <div className="flex items-center gap-3 mt-2">
            <span className="text-sm text-muted-foreground">{signal.pair} · {signal.timeframe}</span>
            <ProgressBar value={signal.confidenceScore * 100} color="amber" className="w-32" />
            <span className="text-sm font-mono">{(signal.confidenceScore * 100).toFixed(0)}%</span>
          </div>
          {signal.cautionNotes && signal.cautionNotes.length > 0 && (
            <ul className="mt-2 space-y-1">
              {signal.cautionNotes.map((note, i) => (
                <li key={i} className="text-xs text-amber-600 dark:text-amber-400">⚠ {note}</li>
              ))}
            </ul>
          )}
          <span className="text-xs text-muted-foreground">{formatTimestamp(signal.timestamp)}</span>
        </div>
      )}

      {state === 'no-signal' && (
        <div className="flex flex-col items-center gap-3 py-8">
          <span className="text-muted-foreground">No pending signal</span>
          <Button variant="ghost" onClick={onTriggerAnalysis}>
            Trigger New Analysis
          </Button>
        </div>
      )}

      {state === 'monitoring' && (
        <div className="flex flex-col items-center gap-2 py-8">
          <span className="text-muted-foreground">Signal processed — monitoring position</span>
        </div>
      )}
    </div>
  )
}
