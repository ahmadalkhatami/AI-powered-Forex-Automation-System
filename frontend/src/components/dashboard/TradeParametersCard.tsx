import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import type { TradeParametersData } from '@/lib/types'

interface TradeParametersCardProps {
  params: TradeParametersData | null
}

export function TradeParametersCard({ params }: TradeParametersCardProps) {
  if (!params) {
    return (
      <Card className="opacity-50">
        <CardContent className="py-8 text-center text-sm text-muted-foreground">
          No signal — parameters will appear here
        </CardContent>
      </Card>
    )
  }

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-semibold uppercase tracking-wider text-muted-foreground">
          Trade Parameters
        </CardTitle>
      </CardHeader>
      <CardContent>
        <div className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-3">
          <span className="text-xs text-muted-foreground self-center">Entry</span>
          <span className="font-mono font-bold text-2xl">{params.entry.toFixed(4)}</span>

          <span className="text-xs text-muted-foreground self-center">Stop Loss</span>
          <div className="flex items-baseline gap-2">
            <span className="font-mono font-bold text-2xl text-red-600 dark:text-red-400">{params.stopLoss.toFixed(4)}</span>
            <span className="text-xs text-muted-foreground">(-{params.stopLossPips} pips)</span>
          </div>

          <span className="text-xs text-muted-foreground self-center">Take Profit</span>
          <div className="flex items-baseline gap-2">
            <span className="font-mono font-bold text-2xl text-emerald-600 dark:text-emerald-400">{params.takeProfit.toFixed(4)}</span>
            <span className="text-xs text-muted-foreground">(+{params.takeProfitPips} pips)</span>
          </div>

          <span className="text-xs text-muted-foreground self-center">Lot Size</span>
          <span className="font-mono font-bold text-2xl">{params.lotSize.toFixed(2)}</span>

          <span className="text-xs text-muted-foreground self-center">Risk</span>
          <div className="flex items-baseline gap-2">
            <span className="font-mono font-bold text-2xl text-amber-600 dark:text-amber-400">${params.riskAmount.toFixed(2)}</span>
            <span className="text-xs text-muted-foreground">({params.riskPercent.toFixed(2)}%)</span>
          </div>

          <span className="text-xs text-muted-foreground self-center">R:R Ratio</span>
          <span className="font-mono font-bold text-2xl">1:{params.riskRewardRatio.toFixed(3)}</span>
        </div>
      </CardContent>
    </Card>
  )
}
