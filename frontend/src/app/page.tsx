'use client'

import { useState, useEffect, useCallback, useRef } from 'react'
import { RefreshCw, Cpu } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { SignalHero } from '@/components/dashboard/SignalHero'
import { TradeParametersCard } from '@/components/dashboard/TradeParametersCard'
import { ApproveRejectActions } from '@/components/dashboard/ApproveRejectActions'
import { SignalAnalysisPanel } from '@/components/dashboard/SignalAnalysisPanel'
import { RiskGatePanel } from '@/components/dashboard/RiskGatePanel'
import { AccountHealthBar } from '@/components/dashboard/AccountHealthBar'
import { PositionsList } from '@/components/dashboard/PositionsList'
import { CandlestickChart } from '@/components/dashboard/CandlestickChart'
import { MifxLiveTicker } from '@/components/dashboard/MifxLiveTicker'
import { useToast } from '@/hooks/use-toast'
import {
  analyzeSignal,
  evaluateRisk,
  executeTrade,
  getAllPositions,
  getCandles,
  getAccountHealth,
  getMifxStatus,
  closePosition,
  deployEa,
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
  CandleBar,
  MifxStatusResponse,
} from '@/lib/types'

const INITIAL_EQUITY = 1_000
const PAIR = 'EURUSD'
const TIMEFRAME = '1D'

type PageState = 'loading' | 'no-signal' | 'signal-ready' | 'processing' | 'monitoring' | 'error' | 'ea-update-required'

function mapTradeParameters(p: TradeParametersResponse, equity: number): TradeParametersData {
  return {
    entry: p.entry,
    stopLoss: p.stopLoss,
    stopLossPips: p.stopLossPips,
    takeProfit: p.takeProfit,
    takeProfitPips: p.takeProfitPips,
    lotSize: p.lotSize,
    riskAmount: p.riskAmount,
    riskPercent: equity > 0 ? (p.riskAmount / equity) * 100 : 0,
    potentialProfit: p.potentialProfit,
    riskRewardRatio: p.riskRewardRatio,
  }
}

function mapToSignalHeroData(
  signal: TradeSignalResponse,
  risk: RiskValidationResponse | null,
  equity: number,
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
    parameters: mapTradeParameters(signal.parameters, equity),
  }
}

function mapToSignalAnalysisData(signal: TradeSignalResponse): SignalAnalysisData {
  return {
    trendScore: signal.trend.score,
    trendBias: signal.trend.bias,
    trendStrength: signal.trend.strength,
    trendRationale: signal.trend.scoreRationale,
    momentumScore: signal.momentum.score,
    momentumRSI: signal.momentum.rsiValue,
    momentumDirection: signal.momentum.rsiDirection,
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
  equity: number,
  drawdownPct: number,
): RiskGatePanelData {
  const params = risk.validatedParameters
  return {
    decision: risk.decision,
    equity,
    drawdownPct,
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

export default function DashboardPage() {
  const { toast } = useToast()

  const [pageState, setPageState] = useState<PageState>('no-signal')
  const [eaDeploying, setEaDeploying] = useState(false)
  const [rawSignal, setRawSignal] = useState<TradeSignalResponse | null>(null)
  const [riskValidation, setRiskValidation] = useState<RiskValidationResponse | null>(null)
  const [positions, setPositions] = useState<TradePositionResponse[]>([])
  const [candles, setCandles] = useState<CandleBar[]>([])
  const [mifxStatus, setMifxStatus] = useState<MifxStatusResponse | null>(null)
  const livePollInFlight = useRef(false)
  const [accountHealth, setAccountHealth] = useState<AccountHealthData>({
    equity: INITIAL_EQUITY,
    peakEquity: INITIAL_EQUITY,
    drawdownPct: 0,
    openPositions: 0,
    maxPositions: 3,
    totalTrades: 0,
    winRate: 0,
    source: 'SIMULATION',
  })

  const refreshAccountHealth = useCallback(async () => {
    try {
      const health = await getAccountHealth()
      setAccountHealth({
        equity: health.equity,
        peakEquity: health.peakEquity,
        drawdownPct: health.drawdownPct,
        openPositions: health.openPositions,
        maxPositions: health.maxPositions,
        totalTrades: health.totalTrades,
        winRate: health.winRate,
        source: health.source,
        riskTier: health.riskTier,
        riskPerTradePct: health.riskPerTradePct,
        dailyCapPct: health.dailyCapPct,
        maxDailyTrades: health.maxDailyTrades,
        dailyRiskUsedUsd: health.dailyRiskUsedUsd,
        tradesOpenedToday: health.tradesOpenedToday,
        dailyCapUtilization: health.dailyCapUtilization,
      })
    } catch {
      // keep previous value
    }
  }, [])

  const refreshPositions = useCallback(async () => {
    try {
      const all = await getAllPositions()
      setPositions(all)
    } catch {
      // keep previous value
    }
  }, [])

  const runFullPipeline = useCallback(async () => {
    setPageState('loading')
    try {
      const [signal, candleData] = await Promise.all([
        analyzeSignal(PAIR, TIMEFRAME),
        getCandles(PAIR, 90),
      ])

      setCandles(candleData)
      setRawSignal(signal)

      const [allPos] = await Promise.all([
        getAllPositions(),
        refreshAccountHealth(),
      ])
      setPositions(allPos)

      const openCount = allPos.filter((p) => p.status === 'ACTIVE').length

      const risk = await evaluateRisk({
        signalId: signal.id,
        finalDecision: signal.signal,
        adjustedConfidence: signal.confidenceScore,
        totalScore: signal.confluenceScore,
        agreementScore: signal.confidenceScore,
        equity: accountHealth.equity,
        openPositions: openCount,
      })
      setRiskValidation(risk)
      setPageState(openCount > 0 ? 'monitoring' : 'signal-ready')
    } catch (err) {
      const detail = err instanceof Error ? err.message : String(err)
      console.error('[pipeline] error:', detail, err)
      // EA_UPDATE_REQUIRED = EA v1.14, belum di-compile ke v1.15
      if (detail.includes('EA_UPDATE_REQUIRED')) {
        setPageState('ea-update-required')
      } else {
        setPageState('error')
        toast({ title: 'Connection Error', description: detail, variant: 'destructive' })
      }
    }
  }, [toast, refreshAccountHealth, accountHealth.equity])

  // Load data ringan saat pertama kali buka (account health + positions, tanpa analisa)
  useEffect(() => {
    Promise.all([refreshAccountHealth(), refreshPositions()]).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Poll MIFX live price + posisi + account health setiap 1 detik.
  useEffect(() => {
    const poll = async () => {
      if (livePollInFlight.current) return
      livePollInFlight.current = true
      try {
        const [s, all, health] = await Promise.all([
          getMifxStatus(),
          getAllPositions(),
          getAccountHealth(),
        ])
        setMifxStatus(s)
        setPositions(all)
        setAccountHealth({
          equity: health.equity,
          peakEquity: health.peakEquity,
          drawdownPct: health.drawdownPct,
          openPositions: health.openPositions,
          maxPositions: health.maxPositions,
          totalTrades: health.totalTrades,
          winRate: health.winRate,
          source: health.source,
          riskTier: health.riskTier,
          riskPerTradePct: health.riskPerTradePct,
          dailyCapPct: health.dailyCapPct,
          maxDailyTrades: health.maxDailyTrades,
          dailyRiskUsedUsd: health.dailyRiskUsedUsd,
          tradesOpenedToday: health.tradesOpenedToday,
          dailyCapUtilization: health.dailyCapUtilization,
        })
      } catch {
        // keep previous value
      } finally {
        livePollInFlight.current = false
      }
    }
    poll()
    const id = setInterval(poll, 1000)
    return () => clearInterval(id)
  }, [])

  const handleApprove = async () => {
    if (!rawSignal || !riskValidation) return
    setPageState('processing')
    try {
      // Mode MIFX_DEMO jika EA terkoneksi, SIMULATION jika tidak
      const tradeMode = mifxStatus?.connected ? 'MIFX_DEMO' : 'SIMULATION'
      const position = await executeTrade({
        signalId: rawSignal.id,
        riskValidation: {
          decision: riskValidation.decision,
          positionDecision: riskValidation.positionDecision,
          validatedParameters: riskValidation.validatedParameters,
          cautionNotes: riskValidation.cautionNotes,
          noGoReasons: riskValidation.noGoReasons,
        },
        peakEquity: accountHealth.peakEquity,
        currentEquity: accountHealth.equity,
        mode: tradeMode,
      })
      await Promise.all([refreshPositions(), refreshAccountHealth()])
      const isSkipped = position.status === 'SKIPPED'
      if (isSkipped) {
        // Skipped by hard limit (max position, drawdown, risk cap)
        setPageState('signal-ready')
        toast({
          title: '⚠ Trade skipped',
          description: position.skipReason ?? 'Ditolak oleh hard limit',
          variant: 'destructive',
        })
      } else {
        setPageState('monitoring')
        const modeLabel = tradeMode === 'MIFX_DEMO' ? '🟢 MIFX' : '🔵 SIMULATION'
        toast({ title: `Position opened (${modeLabel})`, description: `${position.tradeId} — ACTIVE` })
      }
    } catch {
      setPageState('signal-ready')
      toast({ title: 'Execute failed', description: 'Trade could not be executed', variant: 'destructive' })
    }
  }

  const handleDeployEa = async () => {
    setEaDeploying(true)
    try {
      const result = await deployEa()
      toast({
        title: result.success ? '📋 EA Updated' : '❌ Update gagal',
        description: result.message,
        variant: result.success ? 'default' : 'destructive',
        duration: result.success ? 8000 : 5000,
      })
    } catch (err) {
      toast({ title: '❌ Deploy gagal', description: String(err), variant: 'destructive' })
    } finally {
      setEaDeploying(false)
    }
  }

  const handleReject = () => {
    setRawSignal(null)
    setRiskValidation(null)
    setPageState('no-signal')
    toast({ title: 'Signal dismissed', description: 'Run new analysis when ready' })
  }

  const handleClosePosition = async (tradeId: string, outcome: 'WIN' | 'LOSS', exitPrice: number) => {
    try {
      const closed = await closePosition(tradeId, outcome, exitPrice)
      setPositions((prev) => prev.map((p) => p.tradeId === closed.tradeId ? closed : p))
      void Promise.all([refreshPositions(), refreshAccountHealth()])
      toast({
        title: outcome === 'WIN' ? '✅ Trade closed — WIN' : '❌ Trade closed — LOSS',
        description: `Exit @ ${exitPrice.toFixed(5)}`,
      })
    } catch (err) {
      const detail = err instanceof Error ? err.message : 'Close order could not be completed'
      toast({ title: 'Close failed', description: detail, variant: 'destructive' })
    }
  }

  // Derived display data — prefer MIFX live mid price over Yahoo candle close
  const currentPrice = mifxStatus?.connected && mifxStatus.mid
    ? mifxStatus.mid
    : candles[candles.length - 1]?.close
  const capturedAt = rawSignal?.snapshot?.capturedAt
  const regime     = rawSignal?.snapshot?.regime ?? null
  const adx14      = rawSignal?.snapshot?.adX14  ?? null

  const heroData = rawSignal ? mapToSignalHeroData(rawSignal, riskValidation, accountHealth.equity) : null
  const analysisData = rawSignal ? mapToSignalAnalysisData(rawSignal) : null
  const riskGateData = riskValidation
    ? mapToRiskGatePanelData(
        riskValidation,
        positions.filter((p) => p.status === 'ACTIVE').length,
        accountHealth.equity,
        accountHealth.drawdownPct,
      )
    : null
  const paramsData = heroData?.parameters ?? null
  const positionCards = positions
    .filter((p) => p.status !== 'SKIPPED')
    .sort((a, b) => {
      // ACTIVE first, then by closedAt desc
      if (a.status === 'ACTIVE' && b.status !== 'ACTIVE') return -1
      if (a.status !== 'ACTIVE' && b.status === 'ACTIVE') return 1
      return (b.closedAt ?? '').localeCompare(a.closedAt ?? '')
    })
    .map(mapToPositionCard)
  const actionState = deriveActionState(pageState, riskValidation)

  return (
    <div className="grid grid-cols-1 md:grid-cols-[65%_35%] gap-4">
      <div className="space-y-4 pb-24 sm:pb-0">
        {/* Header with pair info + MIFX live ticker + trigger */}
        <div className="flex items-center justify-between gap-2 flex-wrap">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-sm font-medium">EUR/USD</span>
            <span className="text-xs text-muted-foreground">·</span>
            <span className="text-xs text-muted-foreground">Daily</span>
            <span className="text-xs text-muted-foreground">·</span>
            {/* Live MIFX ticker — menggantikan harga statis */}
            <MifxLiveTicker status={mifxStatus} />
            {/* Fallback: tampilkan harga Yahoo jika MIFX offline */}
            {!mifxStatus?.connected && currentPrice && (
              <span className="text-sm font-mono font-semibold text-muted-foreground">
                {currentPrice.toFixed(5)}
              </span>
            )}
            {/* Regime badge — tampil setelah analisis dijalankan */}
            {regime && regime !== 'Unknown' && (
              <span className={cn(
                'px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wide border',
                regime === 'Trending'     && 'bg-emerald-500/10 text-emerald-600 border-emerald-500/30 dark:text-emerald-400',
                regime === 'Ranging'      && 'bg-amber-500/10 text-amber-600 border-amber-500/30 dark:text-amber-400',
                regime === 'Volatile'     && 'bg-red-500/10 text-red-600 border-red-500/30 dark:text-red-400',
                regime === 'Transitional' && 'bg-blue-500/10 text-blue-600 border-blue-500/30 dark:text-blue-400',
              )}>
                {regime} {adx14 !== null && adx14 > 0 ? `ADX ${adx14.toFixed(1)}` : ''}
              </span>
            )}
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={handleDeployEa}
              disabled={eaDeploying}
              className="gap-2"
              title="Copy + compile EA terbaru ke MT5 otomatis"
            >
              <Cpu className={cn('h-3.5 w-3.5', eaDeploying && 'animate-pulse')} />
              {eaDeploying ? 'Compiling…' : 'Update EA'}
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={runFullPipeline}
              disabled={pageState === 'loading' || pageState === 'processing'}
              className="gap-2"
            >
              <RefreshCw className={cn('h-3.5 w-3.5', pageState === 'loading' && 'animate-spin')} />
              Trigger Analysis
            </Button>
          </div>
        </div>

        {/* Candlestick Chart */}
        <CandlestickChart
          candles={candles}
          pair="EUR/USD"
          capturedAt={capturedAt}
        />

        {/* EA update required banner */}
        {pageState === 'ea-update-required' && (
          <div className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-4 space-y-2">
            <p className="text-sm font-semibold text-amber-500">EA v1.15 belum di-compile</p>
            <p className="text-xs text-muted-foreground">
              ForexAI_Bridge masih v1.14 dan belum bisa mengirim indikator (MA/RSI/S/R) ke backend.
            </p>
            <ol className="text-xs text-muted-foreground list-decimal list-inside space-y-1">
              <li>Di MT5, tekan <strong>F4</strong> untuk buka MetaEditor</li>
              <li>File → Open → pilih <strong>ForexAI_Bridge.mq5</strong></li>
              <li>Tekan <strong>F7</strong> untuk compile</li>
              <li>Drag ulang EA ke chart EURUSD.m,M15</li>
              <li>Tekan <strong>Trigger Analysis</strong> lagi</li>
            </ol>
          </div>
        )}

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
              mode={mifxStatus?.connected ? 'MIFX_DEMO' : 'SIMULATION'}
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
        <PositionsList
          positions={positionCards.length > 0 ? positionCards : null}
          currentPrice={currentPrice}
          onClosePosition={handleClosePosition}
        />
      </div>
    </div>
  )
}
