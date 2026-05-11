'use client'

import { useState } from 'react'
import { ChevronDown } from 'lucide-react'
import { ProgressBar, Badge } from '@tremor/react'
import { Card, CardContent } from '@/components/ui/card'
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible'
import { Separator } from '@/components/ui/separator'
import { cn } from '@/lib/utils'
import type { RiskGatePanelData } from '@/lib/types'

type TremorColor = 'emerald' | 'amber' | 'red'

function drawdownColor(pct: number): TremorColor {
  if (pct >= 0.09) return 'red'
  if (pct >= 0.07) return 'amber'
  return 'emerald'
}

const badgeColors: Record<RiskGatePanelData['decision'], TremorColor> = {
  GO: 'emerald',
  GO_WITH_CAUTION: 'amber',
  'NO-GO': 'red',
}

const badgeLabels: Record<RiskGatePanelData['decision'], string> = {
  GO: 'GO ✓',
  GO_WITH_CAUTION: 'GO ⚠',
  'NO-GO': 'NO-GO ✗',
}

interface RiskGatePanelProps {
  data: RiskGatePanelData | null
}

export function RiskGatePanel({ data }: RiskGatePanelProps) {
  const [open, setOpen] = useState(false)

  if (!data) {
    return (
      <div className="rounded-lg border border-border bg-card p-4 opacity-50">
        <span className="text-sm text-muted-foreground">
          Risk gate — will appear when signal is active
        </span>
      </div>
    )
  }

  return (
    <Card
      className={cn(
        'transition-colors',
        data.decision === 'NO-GO' && 'border-red-300 dark:border-red-800',
      )}
    >
      <Collapsible open={open} onOpenChange={setOpen}>
        <CollapsibleTrigger
          className={cn(
            'flex w-full items-center justify-between p-4 hover:bg-muted/50 transition-colors',
            'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-t-lg',
          )}
          aria-label="Toggle risk gate panel"
        >
          <span className="text-sm font-semibold uppercase tracking-wider">
            Risk Gate
          </span>
          <div className="flex items-center gap-2">
            <Badge color={badgeColors[data.decision]} size="sm">
              {badgeLabels[data.decision]}
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
          <CardContent className="pt-0 px-4 pb-4 space-y-3">
            <Separator />

            {/* Account status section */}
            <div className="grid grid-cols-3 gap-4 p-3 bg-muted/30 rounded-md">
              <div>
                <p className="text-xs text-muted-foreground">Equity</p>
                <p className="font-mono font-bold text-sm">
                  ${data.equity.toLocaleString()}
                </p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground mb-1">Drawdown</p>
                <div className="flex items-center gap-1">
                  <ProgressBar
                    value={data.drawdownPct * 100}
                    color={drawdownColor(data.drawdownPct)}
                    className="flex-1"
                  />
                  <span className="text-xs font-mono">
                    {(data.drawdownPct * 100).toFixed(1)}%
                  </span>
                </div>
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Positions</p>
                <p className="font-mono font-bold text-sm">
                  {data.openPositions}/{data.maxPositions}
                </p>
              </div>
            </div>

            {/* NO-GO reasons */}
            {data.decision === 'NO-GO' && data.noGoReasons.length > 0 && (
              <div className="space-y-1">
                {data.noGoReasons.map((reason, i) => (
                  <p key={i} className="text-sm text-red-600 dark:text-red-400 flex items-start gap-2">
                    <span>✗</span>
                    <span>{reason}</span>
                  </p>
                ))}
              </div>
            )}

            {/* GO_WITH_CAUTION notes */}
            {data.decision === 'GO_WITH_CAUTION' && data.cautionNotes.length > 0 && (
              <div className="space-y-1">
                {data.cautionNotes.map((note, i) => (
                  <p key={i} className="text-sm text-amber-600 dark:text-amber-400 flex items-start gap-2">
                    <span>⚠</span>
                    <span>{note}</span>
                  </p>
                ))}
              </div>
            )}

            {/* Validated parameters if present */}
            {(data.validatedEntry || data.validatedStopLoss || data.validatedTakeProfit) && (
              <div className="grid grid-cols-3 gap-2 text-xs text-muted-foreground">
                {data.validatedEntry && (
                  <span>Entry: <span className="font-mono font-semibold text-foreground">{data.validatedEntry.toFixed(4)}</span></span>
                )}
                {data.validatedStopLoss && (
                  <span>SL: <span className="font-mono font-semibold text-red-600 dark:text-red-400">{data.validatedStopLoss.toFixed(4)}</span></span>
                )}
                {data.validatedTakeProfit && (
                  <span>TP: <span className="font-mono font-semibold text-emerald-600 dark:text-emerald-400">{data.validatedTakeProfit.toFixed(4)}</span></span>
                )}
              </div>
            )}
          </CardContent>
        </CollapsibleContent>
      </Collapsible>
    </Card>
  )
}
