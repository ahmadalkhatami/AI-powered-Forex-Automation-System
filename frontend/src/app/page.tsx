'use client'

import { useState, useEffect, useCallback, useRef } from 'react'
import { RefreshCw, Cpu, Bell, BellOff, Zap, ZapOff, OctagonAlert, Play } from 'lucide-react'
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
import { useDashboardStream } from '@/lib/useDashboardStream'
import {
  analyzeSignal,
  evaluateRisk,
  executeTrade,
  getAllPositions,
  getCandles,
  getAccountHealth,
  getMifxStatus,
  closePositionMarket,
  deployEa,
  haltSystem,
  resumeSystem,
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
  ChartTimeframe,
  MifxStatusResponse,
} from '@/lib/types'

const INITIAL_EQUITY = 1_000
const PAIR = 'EURUSD'
// Analyzer pakai indikator M15 (MA, RSI, ATR, ADX) + bias H1+D1 untuk trend confirmation.
// Label "M15" yang dikirim ke backend cuma metadata — tidak mengubah perhitungan.
const TIMEFRAME = 'M15'
const CHART_CANDLE_COUNT = 200

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
    stopLoss: pos.stopLoss,
    takeProfit: pos.takeProfit,
    floatingPnl: pos.floatingPnl,
    floatingPnlPips: pos.floatingPnlPips,
    lotSize: pos.lotSize,
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
  const [chartTimeframe, setChartTimeframe] = useState<ChartTimeframe>('M15')
  const [chartWideMode, setChartWideMode] = useState(false)
  const [mifxStatus, setMifxStatus] = useState<MifxStatusResponse | null>(null)
  const [autoTrigger, setAutoTrigger] = useState(false)
  const [autoApprove, setAutoApprove] = useState(false)
  // Risk override slider — Nano mode only. null = pakai tier default (5%).
  const [nanoRiskOverride, setNanoRiskOverride] = useState<number | null>(null)
  const lastModeRef = useRef<string | null>(null)
  const stream = useDashboardStream()
  const livePollInFlight = useRef(false)
  const lastBarTimeRef = useRef<number | null>(null)
  const notifiedSignalIdRef = useRef<string | null>(null)
  const autoApprovedSignalIdRef = useRef<string | null>(null)
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
        consecutiveLosses: health.consecutiveLosses,
        maxConsecutiveLosses: health.maxConsecutiveLosses,
        isHalted: health.isHalted,
        haltReason: health.haltReason,
        maxSpreadPips: health.maxSpreadPips,
        mode: health.mode,
        isNanoMode: health.isNanoMode,
        effectiveRiskPct: health.effectiveRiskPct,
        nanoMaxDailyLossUsd: health.nanoMaxDailyLossUsd,
        nanoEquityFloorUsd:  health.nanoEquityFloorUsd,
        todayRealizedPnlUsd: health.todayRealizedPnlUsd,
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
        getCandles(PAIR, chartTimeframe, CHART_CANDLE_COUNT),
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
      // EA_UPDATE_REQUIRED = EA versi lama tidak kirim indikator/candle (perlu v1.20+)
      if (detail.includes('EA_UPDATE_REQUIRED')) {
        setPageState('ea-update-required')
      } else {
        setPageState('error')
        toast({ title: 'Connection Error', description: detail, variant: 'destructive' })
      }
    }
  }, [toast, refreshAccountHealth, accountHealth.equity, chartTimeframe])

  // Load data ringan saat pertama kali buka (account health + positions, tanpa analisa)
  useEffect(() => {
    Promise.all([refreshAccountHealth(), refreshPositions()]).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Fetch candle saat awal & saat user ganti timeframe — tanpa nunggu Trigger Analysis.
  // Plus polling tiap 15 detik supaya pick up bar baru yang EA push.
  useEffect(() => {
    const fetchCandles = () =>
      getCandles(PAIR, chartTimeframe, CHART_CANDLE_COUNT)
        .then(setCandles)
        .catch(() => {})
    fetchCandles()
    const id = setInterval(fetchCandles, 15_000)
    return () => clearInterval(id)
  }, [chartTimeframe])

  // Mirror SignalR push → state utama (real-time, sub-detik latency)
  useEffect(() => {
    if (stream.mifxStatus) setMifxStatus(stream.mifxStatus)
  }, [stream.mifxStatus])

  useEffect(() => {
    if (stream.positions) setPositions(stream.positions)
  }, [stream.positions])

  useEffect(() => {
    if (stream.accountHealth) {
      const h = stream.accountHealth
      setAccountHealth({
        equity: h.equity,
        peakEquity: h.peakEquity,
        drawdownPct: h.drawdownPct,
        openPositions: h.openPositions,
        maxPositions: h.maxPositions,
        totalTrades: h.totalTrades,
        winRate: h.winRate,
        source: h.source,
        riskTier: h.riskTier,
        riskPerTradePct: h.riskPerTradePct,
        dailyCapPct: h.dailyCapPct,
        maxDailyTrades: h.maxDailyTrades,
        dailyRiskUsedUsd: h.dailyRiskUsedUsd,
        tradesOpenedToday: h.tradesOpenedToday,
        dailyCapUtilization: h.dailyCapUtilization,
        consecutiveLosses: h.consecutiveLosses,
        maxConsecutiveLosses: h.maxConsecutiveLosses,
        isHalted: h.isHalted,
        haltReason: h.haltReason,
        maxSpreadPips: h.maxSpreadPips,
        mode: h.mode,
        isNanoMode: h.isNanoMode,
        effectiveRiskPct: h.effectiveRiskPct,
        nanoMaxDailyLossUsd: h.nanoMaxDailyLossUsd,
        nanoEquityFloorUsd:  h.nanoEquityFloorUsd,
        todayRealizedPnlUsd: h.todayRealizedPnlUsd,
      })
    }
  }, [stream.accountHealth])

  // Fallback polling — interval lambat (5s) saat SignalR jalan, cepat (1s) saat tidak.
  // Backup buat skenario: hub disconnect / EA stopped / first-load (sebelum push pertama).
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
    const id = setInterval(poll, stream.isConnected ? 5000 : 1000)
    return () => clearInterval(id)
  }, [stream.isConnected])

  // Load auto-trigger + auto-approve preference + request notification permission
  useEffect(() => {
    const savedTrigger = localStorage.getItem('forexai.autoTrigger') === 'true'
    const savedApprove = localStorage.getItem('forexai.autoApprove') === 'true'
    setAutoTrigger(savedTrigger)
    setAutoApprove(savedApprove)
    if (savedTrigger && 'Notification' in window && Notification.permission === 'default') {
      Notification.requestPermission().catch(() => {})
    }
    // Load chart wide-mode preference
    setChartWideMode(localStorage.getItem('forexai.chartWideMode') === 'true')
  }, [])

  const toggleChartWideMode = () => {
    setChartWideMode((prev) => {
      const next = !prev
      localStorage.setItem('forexai.chartWideMode', String(next))
      return next
    })
  }

  // Default OFF auto-trigger + auto-approve saat MASUK ke REAL mode (sekali per mode change).
  // User harus manual approve di real money mode sampai confident.
  useEffect(() => {
    const currentMode = accountHealth.mode
    if (!currentMode) return
    if (lastModeRef.current === currentMode) return  // not a transition
    lastModeRef.current = currentMode

    if (currentMode === 'REAL') {
      setAutoTrigger(false)
      setAutoApprove(false)
      localStorage.setItem('forexai.autoTrigger', 'false')
      localStorage.setItem('forexai.autoApprove', 'false')
      toast({
        title: '🔴 REAL MODE detected',
        description: 'Auto-trigger + auto-approve di-OFF default. Manual approve untuk safety.',
      })
    }
  }, [accountHealth.mode, toast])

  const toggleAutoApprove = useCallback(() => {
    const next = !autoApprove
    setAutoApprove(next)
    localStorage.setItem('forexai.autoApprove', String(next))
    toast({
      title: next ? '⚡ Auto-approve aktif' : '🛑 Auto-approve nonaktif',
      description: next
        ? 'Signal lolos vetos + confidence ≥ 70% akan auto-execute'
        : 'Kembali butuh klik Approve manual',
      variant: next ? 'default' : 'default',
    })
  }, [autoApprove, toast])

  const toggleAutoTrigger = useCallback(() => {
    const next = !autoTrigger
    setAutoTrigger(next)
    localStorage.setItem('forexai.autoTrigger', String(next))
    if (next && 'Notification' in window && Notification.permission === 'default') {
      Notification.requestPermission().then((perm) => {
        if (perm === 'granted') {
          toast({ title: '🔔 Auto-trigger aktif', description: 'Akan analisa setiap bar baru + notif GO' })
        }
      }).catch(() => {})
    } else {
      toast({
        title: next ? '🔔 Auto-trigger aktif' : '🔕 Auto-trigger nonaktif',
        description: next ? 'Akan analisa setiap bar baru' : 'Kembali manual',
      })
    }
  }, [autoTrigger, toast])

  // Auto-trigger analysis saat new bar terdeteksi (M15/H1/D1 sesuai chartTimeframe)
  useEffect(() => {
    if (candles.length === 0) return
    const lastTime = candles[candles.length - 1].time
    if (lastBarTimeRef.current === null) {
      lastBarTimeRef.current = lastTime
      return
    }
    if (lastTime <= lastBarTimeRef.current) return

    lastBarTimeRef.current = lastTime
    if (!autoTrigger) return

    const hasActive = positions.some((p) => p.status === 'ACTIVE')
    if (hasActive) return
    if (pageState === 'loading' || pageState === 'processing') return

    runFullPipeline()
  }, [candles, autoTrigger, positions, pageState, runFullPipeline])

  // Browser notification saat GO signal masuk (sekali per signal ID)
  useEffect(() => {
    if (!rawSignal || !riskValidation) return
    if (riskValidation.decision === 'NO-GO') return
    if (rawSignal.signal === 'HOLD') return
    if (notifiedSignalIdRef.current === rawSignal.id) return
    if (typeof window === 'undefined' || !('Notification' in window)) return
    if (Notification.permission !== 'granted') return

    notifiedSignalIdRef.current = rawSignal.id
    const conf = (rawSignal.confidenceScore * 100).toFixed(0)
    const tagline = riskValidation.decision === 'GO_WITH_CAUTION' ? '⚠ GO_WITH_CAUTION' : '✅ GO'
    new Notification(`🎯 ${rawSignal.signal} ${rawSignal.pair} — ${tagline}`, {
      body: `Entry ${rawSignal.parameters.entry.toFixed(5)} · SL ${rawSignal.parameters.stopLoss.toFixed(5)} · TP ${rawSignal.parameters.takeProfit.toFixed(5)} · Confidence ${conf}%`,
      icon: '/favicon.ico',
      tag: 'forexai-signal',
    })
  }, [rawSignal, riskValidation])

  const handleApprove = useCallback(async () => {
    if (!rawSignal || !riskValidation) return
    setPageState('processing')
    try {
      // Mode MIFX_DEMO jika EA terkoneksi, SIMULATION jika tidak
      const tradeMode = mifxStatus?.connected ? 'MIFX_DEMO' : 'SIMULATION'
      // Risk override hanya kirim kalau Nano mode + slider berbeda dari default
      const overrideToSend = accountHealth.isNanoMode && nanoRiskOverride !== null
        ? nanoRiskOverride
        : undefined
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
        riskPctOverride: overrideToSend,
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
  }, [rawSignal, riskValidation, mifxStatus, accountHealth, nanoRiskOverride, refreshPositions, refreshAccountHealth, toast])

  // Auto-approve: confidence ≥ 70% (single gate, data-driven).
  // Backtest 200 candle M15 menunjukkan vetos (structure/RSI/overextension) sudah jadi
  // quality gate utama — filter tambahan justru buang win-win. Threshold 70% adalah
  // sanity floor (di atas median consensus 50-65% untuk mediocre setups).
  useEffect(() => {
    if (!autoApprove) return
    if (!rawSignal || !riskValidation) return
    if (autoApprovedSignalIdRef.current === rawSignal.id) return
    if (riskValidation.decision === 'NO-GO') return
    if (rawSignal.signal !== 'BUY' && rawSignal.signal !== 'SELL') return
    if (rawSignal.confidenceScore < 0.70) return
    if (pageState === 'processing' || pageState === 'monitoring') return
    if (positions.some((p) => p.status === 'ACTIVE')) return

    autoApprovedSignalIdRef.current = rawSignal.id
    const conf = (rawSignal.confidenceScore * 100).toFixed(0)
    toast({
      title: '🤖 Auto-approve fire',
      description: `${rawSignal.signal} ${rawSignal.pair} · Confidence ${conf}% ≥ 70% + vetos passed — eksekusi otomatis`,
    })
    handleApprove()
  }, [autoApprove, rawSignal, riskValidation, pageState, positions, handleApprove, toast])

  const handleHalt = async () => {
    const confirmed = typeof window !== 'undefined' &&
      window.confirm('Tutup SEMUA posisi aktif + matikan auto-trading?\n\nIni emergency stop — bisa di-resume manual setelah review.')
    if (!confirmed) return
    try {
      const result = await haltSystem('Manual emergency halt from dashboard', true)
      setAutoTrigger(false)
      setAutoApprove(false)
      localStorage.setItem('forexai.autoTrigger', 'false')
      localStorage.setItem('forexai.autoApprove', 'false')
      toast({
        title: '🛑 SYSTEM HALTED',
        description: `Closed ${result.closedCount} positions${result.failures.length > 0 ? ` · ${result.failures.length} gagal` : ''}`,
        variant: 'destructive',
        duration: 10_000,
      })
    } catch (err) {
      toast({
        title: 'Halt gagal',
        description: err instanceof Error ? err.message : 'Unknown error',
        variant: 'destructive',
      })
    }
  }

  const handleResume = async () => {
    try {
      await resumeSystem()
      toast({ title: '✅ System resumed', description: 'Trading kembali aktif' })
    } catch (err) {
      toast({
        title: 'Resume gagal',
        description: err instanceof Error ? err.message : 'Unknown error',
        variant: 'destructive',
      })
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

  const handleCloseMarket = async (tradeId: string) => {
    try {
      const closed = await closePositionMarket(tradeId)
      setPositions((prev) => prev.map((p) => p.tradeId === closed.tradeId ? closed : p))
      void Promise.all([refreshPositions(), refreshAccountHealth()])
      const win = closed.status === 'CLOSED_WIN'
      toast({
        title: win ? '✅ Trade closed — WIN' : '❌ Trade closed — LOSS',
        description: `P&L $${closed.floatingPnl.toFixed(2)} (${closed.floatingPnlPips >= 0 ? '+' : ''}${closed.floatingPnlPips}p)`,
      })
    } catch (err) {
      const detail = err instanceof Error ? err.message : 'Close order could not be completed'
      toast({ title: 'Close failed', description: detail, variant: 'destructive' })
    }
  }

  // Derived display data — prefer MIFX live mid price over last-candle close
  const currentPrice = mifxStatus?.connected && mifxStatus.mid
    ? mifxStatus.mid
    : candles[candles.length - 1]?.close
  const regime     = rawSignal?.snapshot?.regime ?? null
  const adx14      = rawSignal?.snapshot?.adX14  ?? null

  // Overlay TP/SL/Entry untuk chart — prioritas: posisi aktif > validated params > signal params
  // Auto-draw saat sinyal generated: tradingview-style box muncul otomatis di chart.
  const activePosition = positions.find((p) => p.status === 'ACTIVE')
  const tradeOverlay = activePosition
    ? {
        entry:           activePosition.entry,
        stopLoss:        activePosition.stopLoss,
        takeProfit:      activePosition.takeProfit,
        direction:       activePosition.direction === 'SELL' ? ('SELL' as const) : ('BUY' as const),
        status:          'active' as const,
        livePnlPips:     activePosition.floatingPnlPips,
        livePnlUsd:      activePosition.floatingPnl,
        lotSize:         activePosition.lotSize,
        riskAmount:      activePosition.riskAmount,
        potentialProfit: activePosition.potentialProfit,
        riskReward:      activePosition.riskReward,
        anchorTime:      activePosition.openedAt
          ? Math.floor(new Date(activePosition.openedAt).getTime() / 1000)
          : undefined,
      }
    : riskValidation?.validatedParameters && rawSignal && rawSignal.signal !== 'HOLD'
      ? {
          entry:           riskValidation.validatedParameters.entry,
          stopLoss:        riskValidation.validatedParameters.stopLoss,
          takeProfit:      riskValidation.validatedParameters.takeProfit,
          direction:       rawSignal.signal,
          status:          'pending' as const,
          lotSize:         riskValidation.validatedParameters.lotSize,
          riskAmount:      riskValidation.validatedParameters.riskAmount,
          potentialProfit: riskValidation.validatedParameters.potentialProfit,
          riskReward:      riskValidation.validatedParameters.riskRewardRatio,
          confidence:      Math.round((rawSignal.confidenceScore ?? 0) * 100),
        }
      : rawSignal && rawSignal.signal !== 'HOLD'
        ? {
            entry:           rawSignal.parameters.entry,
            stopLoss:        rawSignal.parameters.stopLoss,
            takeProfit:      rawSignal.parameters.takeProfit,
            direction:       rawSignal.signal,
            status:          'pending' as const,
            lotSize:         rawSignal.parameters.lotSize,
            riskAmount:      rawSignal.parameters.riskAmount,
            potentialProfit: rawSignal.parameters.potentialProfit,
            riskReward:      rawSignal.parameters.riskRewardRatio,
            confidence:      Math.round((rawSignal.confidenceScore ?? 0) * 100),
          }
        : null

  const structureOverlay = rawSignal
    ? {
        nearestSupport:    rawSignal.structure.nearestSupport,
        nearestResistance: rawSignal.structure.nearestResistance,
      }
    : null

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
    <div className={cn(
      'grid grid-cols-1 gap-4',
      chartWideMode ? 'md:grid-cols-1' : 'md:grid-cols-[65%_35%]',
    )}>
      <div className="space-y-4 pb-24 sm:pb-0">
        {/* Header with pair info + MIFX live ticker + trigger */}
        <div className="flex items-center justify-between gap-2 flex-wrap">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-sm font-medium">EUR/USD</span>
            <span className="text-xs text-muted-foreground">·</span>
            <span className="text-xs text-muted-foreground">{chartTimeframe}</span>
            <span className="text-xs text-muted-foreground">·</span>
            {/* Live MIFX ticker — menggantikan harga statis */}
            <MifxLiveTicker status={mifxStatus} />
            {/* Fallback: last-candle close jika MIFX offline */}
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
            {/* Mode banner — auto-detect dari EA (DEMO/REAL). Wajib tampil untuk safety. */}
            {accountHealth.mode && (
              <span className={cn(
                'px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-wide border',
                accountHealth.mode === 'REAL'
                  ? 'bg-red-500/15 text-red-600 border-red-500/40 dark:text-red-400'
                  : 'bg-emerald-500/15 text-emerald-600 border-emerald-500/40 dark:text-emerald-400'
              )}
              title={accountHealth.mode === 'REAL'
                ? 'REAL MONEY — auto-detect dari MT5 account'
                : 'DEMO account — practice mode'}>
                {accountHealth.mode === 'REAL' ? '🔴 REAL' : '🟢 DEMO'}
                {' · '}{(accountHealth.riskTier ?? 'starter').toUpperCase()}
                {accountHealth.isNanoMode && (
                  <span className="ml-1 text-[10px] opacity-90">⚠ {((accountHealth.effectiveRiskPct ?? 0.05) * 100).toFixed(1)}%/trade</span>
                )}
              </span>
            )}
            {/* Nano $ cap status — tampil progress today PnL vs daily $ loss limit */}
            {accountHealth.isNanoMode && (accountHealth.nanoMaxDailyLossUsd ?? 0) > 0 && (() => {
              const used = Math.abs(Math.min(accountHealth.todayRealizedPnlUsd ?? 0, 0))
              const cap  = accountHealth.nanoMaxDailyLossUsd ?? 5
              const pct  = Math.min(used / cap, 1)
              const danger = pct >= 0.6
              return (
                <span className={cn(
                  'px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-wide border',
                  danger
                    ? 'bg-red-500/15 text-red-600 border-red-500/40 dark:text-red-400 animate-pulse'
                    : 'bg-amber-500/10 text-amber-600 border-amber-500/30 dark:text-amber-400',
                )}
                title={`Daily loss budget: -$${cap.toFixed(2)}. Saat habis = auto-halt sampai UTC midnight. Floor equity: $${(accountHealth.nanoEquityFloorUsd ?? 20).toFixed(0)}.`}>
                  Daily: -${used.toFixed(2)}/${cap.toFixed(0)}
                  {accountHealth.todayRealizedPnlUsd && accountHealth.todayRealizedPnlUsd > 0 ? (
                    <span className="ml-1 text-emerald-600 dark:text-emerald-400">(+${accountHealth.todayRealizedPnlUsd.toFixed(2)})</span>
                  ) : null}
                </span>
              )
            })()}
          </div>
          <div className="flex items-center gap-2">
            {accountHealth.isHalted ? (
              <Button
                variant="outline"
                size="sm"
                onClick={handleResume}
                className="gap-2 border-emerald-500/60 text-emerald-600 hover:bg-emerald-500/10 dark:text-emerald-400"
                title="Resume trading dari halt state"
              >
                <Play className="h-3.5 w-3.5" />
                Resume
              </Button>
            ) : (
              <Button
                variant="outline"
                size="sm"
                onClick={handleHalt}
                className="gap-2 border-red-500/60 text-red-600 hover:bg-red-500/10 dark:text-red-400"
                title="Emergency stop: close semua active positions + matikan auto-trading"
              >
                <OctagonAlert className="h-3.5 w-3.5" />
                Halt
              </Button>
            )}
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
              onClick={toggleAutoTrigger}
              className={cn('gap-2', autoTrigger && 'border-emerald-500/60 text-emerald-600 dark:text-emerald-400')}
              title={autoTrigger ? 'Klik untuk matikan auto-trigger' : 'Auto-analyze tiap bar baru + browser notif'}
            >
              {autoTrigger ? <Bell className="h-3.5 w-3.5" /> : <BellOff className="h-3.5 w-3.5" />}
              {autoTrigger ? 'Auto ON' : 'Auto OFF'}
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={toggleAutoApprove}
              className={cn('gap-2', autoApprove && 'border-amber-500/60 text-amber-600 dark:text-amber-400')}
              title={autoApprove
                ? 'Auto-execute aktif: vetos passed + confidence ≥ 70%'
                : 'Klik untuk aktifkan auto-execute'}
            >
              {autoApprove ? <Zap className="h-3.5 w-3.5" /> : <ZapOff className="h-3.5 w-3.5" />}
              {autoApprove ? 'Exec ≥70%' : 'Exec OFF'}
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
          timeframe={chartTimeframe}
          livePrice={mifxStatus?.connected ? mifxStatus.mid : null}
          tradeOverlay={tradeOverlay}
          structure={structureOverlay}
          positions={positions}
          onTimeframeChange={setChartTimeframe}
          isMifxConnected={mifxStatus?.connected ?? false}
          isWideMode={chartWideMode}
          onToggleWideMode={toggleChartWideMode}
        />

        {/* EA update required banner */}
        {pageState === 'ea-update-required' && (
          <div className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-4 space-y-2">
            <p className="text-sm font-semibold text-amber-500">EA belum versi terbaru (v1.20+)</p>
            <p className="text-xs text-muted-foreground">
              ForexAI_Bridge belum mengirim indikator/candle lengkap ke backend. Perlu compile ulang.
            </p>
            <ol className="text-xs text-muted-foreground list-decimal list-inside space-y-1">
              <li>Klik tombol <strong>Update EA</strong> di atas (auto copy + compile), <em>atau</em>:</li>
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
            {/* Nano risk slider — hanya tampil saat REAL + tier nano */}
            {accountHealth.isNanoMode && riskValidation?.validatedParameters && (
              <div className="mb-3 p-3 rounded-lg border border-red-500/40 bg-red-500/5 space-y-2">
                <div className="flex items-center justify-between text-xs">
                  <span className="font-semibold text-red-600 dark:text-red-400">⚠ NANO MODE — Risk Override</span>
                  <span className="font-mono text-muted-foreground">
                    {((nanoRiskOverride ?? accountHealth.effectiveRiskPct ?? 0.05) * 100).toFixed(1)}% ·
                    ${(accountHealth.equity * (nanoRiskOverride ?? accountHealth.effectiveRiskPct ?? 0.05)).toFixed(2)}/trade
                  </span>
                </div>
                <input
                  type="range"
                  min={1}
                  max={10}
                  step={0.5}
                  value={(nanoRiskOverride ?? accountHealth.effectiveRiskPct ?? 0.05) * 100}
                  onChange={(e) => setNanoRiskOverride(parseFloat(e.target.value) / 100)}
                  className="w-full accent-red-500"
                />
                <div className="flex items-center justify-between text-[10px] text-muted-foreground">
                  <span>1% (paling aman)</span>
                  <span>5% (default Nano)</span>
                  <span>10% (paling agresif)</span>
                </div>
              </div>
            )}
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

      <div className={cn(
        'space-y-4',
        // Wide mode: account + positions side-by-side di bawah chart supaya tidak terlalu lebar
        chartWideMode && 'md:grid md:grid-cols-2 md:gap-4 md:space-y-0',
      )}>
        <AccountHealthBar
          data={accountHealth}
          positions={positions}
          baselineEquity={INITIAL_EQUITY}
        />
        <PositionsList
          positions={positionCards.length > 0 ? positionCards : null}
          bid={mifxStatus?.bid ?? undefined}
          ask={mifxStatus?.ask ?? undefined}
          lastTickAt={mifxStatus?.time ?? undefined}
          onCloseMarket={handleCloseMarket}
        />
      </div>
    </div>
  )
}
