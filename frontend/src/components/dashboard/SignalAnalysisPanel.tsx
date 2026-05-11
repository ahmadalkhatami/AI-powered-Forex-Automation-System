'use client'

import { useState } from 'react'
import { ChevronDown } from 'lucide-react'
import { ProgressBar } from '@tremor/react'
import { Badge } from '@/components/ui/badge'
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible'
import { Separator } from '@/components/ui/separator'
import { cn } from '@/lib/utils'
import type { SignalAnalysisData } from '@/lib/types'

type TremorColor = 'emerald' | 'amber' | 'red'

function scoreColor(score: number): TremorColor {
  if (score >= 0.70) return 'emerald'
  if (score >= 0.50) return 'amber'
  return 'red'
}

interface ScoreRowProps {
  label: string
  score: number
  subtitle: string
  rationale: string
}

function ScoreRow({ label, score, subtitle, rationale }: ScoreRowProps) {
  return (
    <div className="py-2">
      <div className="grid grid-cols-[80px_1fr_40px] gap-3 items-center">
        <span className="text-xs text-muted-foreground font-medium">{label}</span>
        <div className="space-y-1">
          <ProgressBar value={score * 100} color={scoreColor(score)} />
          <p className="text-xs text-muted-foreground">{subtitle}</p>
        </div>
        <span className="text-sm font-mono font-bold text-right">
          {(score * 100).toFixed(0)}%
        </span>
      </div>
      <p className="mt-1 ml-[92px] text-xs text-muted-foreground">{rationale}</p>
    </div>
  )
}

interface SignalAnalysisPanelProps {
  data: SignalAnalysisData | null
}

export function SignalAnalysisPanel({ data }: SignalAnalysisPanelProps) {
  const [open, setOpen] = useState(false)

  if (!data) {
    return (
      <div className="rounded-lg border border-border bg-card p-4 opacity-50">
        <span className="text-sm text-muted-foreground">
          Signal analysis — will appear when signal is active
        </span>
      </div>
    )
  }

  return (
    <Collapsible
      open={open}
      onOpenChange={setOpen}
      className="rounded-lg border border-border bg-card"
    >
      <CollapsibleTrigger
        className={cn(
          'flex w-full items-center justify-between p-4 hover:bg-muted/50 transition-colors',
          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg',
        )}
        aria-label="Toggle signal analysis panel"
      >
        <span className="text-sm font-semibold uppercase tracking-wider">
          Signal Analysis
        </span>
        <div className="flex items-center gap-2">
          <Badge variant="secondary">
            {(data.confidenceScore * 100).toFixed(0)}% confidence
          </Badge>
          <ChevronDown
            aria-hidden="true"
            className={cn(
              'h-4 w-4 text-muted-foreground transition-transform duration-200',
              open && 'rotate-180',
            )}
          />
        </div>
      </CollapsibleTrigger>

      <CollapsibleContent>
        <div className="px-4 pb-4 space-y-1">
          <Separator className="mb-3" />

          <ScoreRow
            label="Trend"
            score={data.trendScore}
            subtitle={`${data.trendBias} · ${data.trendStrength}`}
            rationale={data.trendRationale}
          />
          <ScoreRow
            label="Momentum"
            score={data.momentumScore}
            subtitle={`RSI ${data.momentumRSI} · ${data.momentumDirection}`}
            rationale={data.momentumRationale}
          />
          <ScoreRow
            label="Structure"
            score={data.structureScore}
            subtitle={data.structurePattern}
            rationale={data.structureRationale}
          />

          <Separator className="my-3" />

          {/* Predictor summary */}
          <div className="grid grid-cols-[80px_1fr_40px] gap-3 items-center py-2">
            <span className="text-xs text-muted-foreground font-medium">Predictor</span>
            <div className="space-y-1">
              <ProgressBar
                value={data.predictorScore}
                color={data.predictorScore >= 70 ? 'emerald' : data.predictorScore >= 50 ? 'amber' : 'red'}
              />
              <p className="text-xs text-muted-foreground">
                Agreement {(data.agreementScore * 100).toFixed(0)}%
              </p>
            </div>
            <span className="text-sm font-mono font-bold text-right">
              {data.predictorScore}
            </span>
          </div>

          {/* Warnings */}
          {data.warnings.length > 0 && (
            <div className="mt-3 space-y-1">
              {data.warnings.map((w, i) => (
                <p key={i} className="text-xs text-amber-600 dark:text-amber-400">
                  ⚠ {w}
                </p>
              ))}
            </div>
          )}
        </div>
      </CollapsibleContent>
    </Collapsible>
  )
}
