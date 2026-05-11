'use client'

import { useState, useEffect, useCallback } from 'react'
import { RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { SignalHero } from '@/components/dashboard/SignalHero'
import { TradeParametersCard } from '@/components/dashboard/TradeParametersCard'
import { ApproveRejectActions } from '@/components/dashboard/ApproveRejectActions'
import { SignalAnalysisPanel } from '@/components/dashboard/SignalAnalysisPanel'
import { RiskGatePanel } from '@/components/dashboard/RiskGatePanel'
import { AccountHealthBar } from '@/components/dashboard/AccountHealthBar'
import { PositionsList } from '@/components/dashboard/PositionsList'
import { useToast } from '@/hooks/use-toast'
import {
  analyzeSignal,
  evaluateRisk,
  executeTrade,
  getPositionStatus,
} from '@/lib/api'
import type {
  SignalHeroData,
  TradeParametersData,
  SignalAnalysisData,
  RiskGatePanelData,
  AccountHealthData,
  PositionCardData,
  ActionState,
  TradeSignalResponse,
  RiskValidationResponse,
  TradePositionResponse,
  TradeParametersResponse,
} from '@/lib/types'

const DEFAULT_EQUITY = 10_000
const DEFAULT_PEAK_EQUITY = 10_000
const PAIR = 'EURUSD'
const TIMEFRAME = 'M15'

type PageState = 'loading' | 'no-signal' | 'signal-ready' | 'processing' | 'monitoring' | 'error'

// --- Data adapter functions ---

function mapTradeParameters(p: TradeParametersResponse): TradeParametersData {
  return {
    entry: p.entry,
    stopLoss: p.stopLoss,
    stopLossPips: p.stopLossPips,
    takeProfit: p.takeProfit,
    takeProfitPips: p.takeProfitPips,
    lotSize: p.lotSize,
    riskAmount: p.riskAmount,
    riskPercent: (p.riskAmount / DEFAULT_EQUITY) * 100,
    potentialProfit: p.potentialProfit,
    riskRewardRatio: p.riskRewardRatio,
  }
}

function mapToSignalHeroData(
  signal: TradeSignalResponse,
  risk: RiskValidationResponse | null,
): SignalHeroData {
  return {
    id: signal.id,
    pair: signal.pair,
    timeframe: signal.timeframe,
    signal: signal.signal,
    decision: risk?.decision ?? 'GO',
    confidenceScore: signal.confidenceScore,
    confluenceScore: signal.confluenceScore / 100,
    timestamp: signal.timestamp,
    cautionNotes: risk?.cautionNotes ?? [],
    blockReason: risk?.noGoReasons?.[0],
    parameters: mapTradeParameters(signal.parameters),
  }
}

function mapToSignalAnalysisData(
  signal: TradeSignalResponse,
): SignalAnalysisData {
  return {
    trendScore: signal.trend.score,
    trendBias: signal.trend.bias,
    trendStrength: signal.trend.strength,
    trendRationale: signal.trend.scoreRationale,
    momentumScore: signal.momentum.score,
    momentumRSI: signal.momentum.rSIValue,
    momentumDirection: signal.momentum.rSIDirection,
    momentumRationale: signal.momentum.scoreRationale,
    structureScore: signal.structure.score,
    structurePattern: signal.structure.candlePattern,
    structureRationale: signal.structure.scoreRationale,
    predictorScore: signal.confluenceScore,
    agreementScore: signal.confidenceScore,
    confidenceScore: signal.confidenceScore,
    warnings: signal.warnings,
  }
}

function mapToRiskGatePanelData(
  risk: RiskValidationResponse,
  openPositions: number,
): RiskGatePanelData {
  const params = risk.validatedParameters
  return {
    decision: risk.decision,
    equity: DEFAULT_EQUITY,
    drawdownPct: 0,
    openPositions,
    maxPositions: 3,
    cautionNotes: risk.cautionNotes,
    noGoReasons: risk.noGoReasons,
    validatedEntry: params?.entry,
    validatedStopLoss: params?.stopLoss,
    validatedTakeProfit: params?.takeProfit,
  }
}

function mapToPositionCard(pos: TradePositionResponse): PositionCardData {
  const direction = pos.direction === 'HOLD' ? 'BUY' : (pos.direction as 'BUY' | 'SELL')
  const status =
    pos.status === 'ACTIVE'
      ? 'ACTIVE'
      : pos.status === 'CLOSED_WIN'
        ? 'CLOSED_WIN'
        : 'CLOSED_LOSS'
  return {
    tradeId: pos.tradeId,
    pair: pos.pair,
    direction,
    entry: pos.entry,
    floatingPnl: pos.floatingPnl,
    floatingPnlPips: pos.floatingPnlPips,
    status,
    closedAt: pos.closedAt ?? undefined,
  }
}

function deriveActionState(
  pageState: PageState,
  risk: RiskValidationResponse | null,
): ActionState {
  if (pageState === 'processing') return 'processing'
  if (!risk) return 'disabled-nogo'
  if (risk.decision === 'NO-GO') return 'disabled-nogo'
  if (risk.decision === 'GO_WITH_CAUTION') return 'enabled-caution'
  return 'enabled-go'
}

// ---

export default function DashboardPage() {
  const { toast } = useToast()

  const [pageState, setPageState] = useState<PageState>('loading')
  const [rawSignal, setRawSignal] = useState<TradeSignalResponse | null>(null)
  const [riskValidation, setRiskValidation] = useState<RiskValidationResponse | null>(null)
  const [positions, setPositions] = useState<TradePositionResponse[]>([])

  const runFullPipeline = useCallback(async () => {
    setPageState('loading')
    try {
      const [signal, existingPos] = await Promise.all([
        analyzeSignal(PAIR, TIMEFRAME),
        getPositionStatus(PAIR),
      ])

      const currentPositions = existingPos && existingPos.status === 'ACTIVE' ? [existingPos] : []
      setPositions(currentPositions)
      setRawSignal(signal)

      const risk = await evaluateRisk({
        signalId: signal.id,
        finalDecision: signal.signal,
        adjustedConfidence: signal.confidenceScore,
        totalScore: signal.confluenceScore,
        agreementScore: signal.confidenceScore,
        equity: DEFAULT_EQUITY,
        openPositions: currentPositions.length,
      })
      setRiskValidation(risk)
      setPageState(currentPositions.length > 0 ? 'monitoring' : 'signal-ready')
    } catch (err) {
      setPageState('error')
      const detail = err instanceof Error ? err.message : String(err)
      console.error('[pipeline] error:', detail, err)
      toast({ title: 'Connection Error', description: detail, variant: 'destructive' })
    }
  }, [toast])

  useEffect(() => {
    runFullPipeline()
  }, [runFullPipeline])

  const handleApprove = async () => {
    if (!rawSignal || !riskValidation) return
    setPageState('processing')
    try {
      const position = await executeTrade({
        signalId: rawSignal.id,
        riskValidation: {
          decision: riskValidation.decision,
          positionDecision: riskValidation.positionDecision,
          validatedParameters: riskValidation.validatedParameters,
          cautionNotes: riskValidation.cautionNotes,
          noGoReasons: riskValidation.noGoReasons,
        },
        peakEquity: DEFAULT_PEAK_EQUITY,
        currentEquity: DEFAULT_EQUITY,
        mode: 'SIMULATION',
      })
      setPositions((prev) => [...prev, position])
      setPageState('monitoring')
      toast({ title: 'Position opened', description: `${position.tradeId} active (SIMULATION)` })
    } catch {
      setPageState('signal-ready')
      toast({ title: 'Execute failed', description: 'Trade could not be executed', variant: 'destructive' })
    }
  }

  const handleReject = () => {
    setRawSignal(null)
    setRiskValidation(null)
    setPageState('no-signal')
    toast({ title: 'Signal dismissed', description: 'Run new analysis when ready' })
  }

  // Derived display data
  const heroData = rawSignal ? mapToSignalHeroData(rawSignal, riskValidation) : null
  const analysisData = rawSignal ? mapToSignalAnalysisData(rawSignal) : null
  const riskGateData = riskValidation
    ? mapToRiskGatePanelData(riskValidation, positions.filter((p) => p.status === 'ACTIVE').length)
    : null
  const paramsData = heroData?.parameters ?? null
  const positionCards = positions
    .filter((p) => p.status !== 'SKIPPED')
    .map(mapToPositionCard)
  const accountHealth: AccountHealthData = {
    equity: DEFAULT_EQUITY,
    peakEquity: DEFAULT_PEAK_EQUITY,
    drawdownPct: 0,
    openPositions: positions.filter((p) => p.status === 'ACTIVE').length,
    maxPositions: 3,
  }
  const actionState = deriveActionState(pageState, riskValidation)

  return (
    <div className="grid grid-cols-1 md:grid-cols-[65%_35%] gap-4">
      <div className="space-y-4 pb-24 sm:pb-0">
        <div className="flex items-center justify-between">
          <span className="text-sm text-muted-foreground">EUR/USD · M15</span>
          <Button
            variant="outline"
            size="sm"
            onClick={runFullPipeline}
            disabled={pageState === 'loading' || pageState === 'processing'}
            className="gap-2"
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Trigger New Analysis
          </Button>
        </div>

        <SignalHero signal={heroData} mode={pageState === 'monitoring' ? 'monitoring' : 'default'} />
        <TradeParametersCard params={paramsData} />

        {heroData && (
          <div className={cn(
            'fixed bottom-0 left-0 right-0 z-10',
            'bg-background/95 backdrop-blur border-t p-4',
            'sm:relative sm:bottom-auto sm:bg-transparent sm:backdrop-blur-none sm:border-0 sm:p-0',
          )}>
            <ApproveRejectActions
              state={actionState}
              signal={heroData}
              onApprove={handleApprove}
              onReject={handleReject}
            />
          </div>
        )}

        <SignalAnalysisPanel data={analysisData} />
        <RiskGatePanel data={riskGateData} />
      </div>

      <div className="space-y-4">
        <AccountHealthBar data={accountHealth} />
        <PositionsList positions={positionCards.length > 0 ? positionCards : null} />
      </div>
    </div>
  )
}
