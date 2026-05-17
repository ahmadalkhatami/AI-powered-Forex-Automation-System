'use client'

import { useEffect, useMemo, useRef, useState } from 'react'
import {
  createChart,
  createSeriesMarkers,
  ColorType,
  CrosshairMode,
  CandlestickSeries,
  LineSeries,
  HistogramSeries,
  LineStyle,
  type IChartApi,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  type IPriceLine,
  type SeriesMarker,
  type Time,
  type UTCTimestamp,
  type CandlestickData,
} from 'lightweight-charts'
import { ChevronDown, ChevronUp, Maximize2, Minimize2 } from 'lucide-react'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { computeSMA, computeRSI } from '@/lib/indicators'
import type { CandleBar, ChartTimeframe, TradePositionResponse } from '@/lib/types'
import { type Drawing, type DrawingStyle, loadDrawings, saveDrawings } from '@/lib/drawings'
import { ChartToolbar, type ToolMode } from './ChartToolbar'
import { ChartDrawingOverlay } from './ChartDrawingOverlay'
import { PositionBoxOverlay, type PositionBox } from './PositionBoxOverlay'

interface TradeOverlay {
  /** Stable ID untuk dismiss tracking (e.g. active-TRADE123, pending-SIGNAL456) */
  id?: string
  entry: number
  stopLoss: number
  takeProfit: number
  direction: 'BUY' | 'SELL'
  /** active = posisi sudah open; pending = signal yang belum di-execute */
  status?: 'active' | 'pending'
  livePnlPips?: number
  livePnlUsd?: number
  lotSize?: number
  riskAmount?: number
  potentialProfit?: number
  riskReward?: number
  /** Confidence 0-100 untuk pending setup label. */
  confidence?: number
  /** Unix timestamp (s). Optional — kalau tidak set, fallback ke last candle time. */
  anchorTime?: number
}

interface StructureOverlay {
  nearestSupport: number
  nearestResistance: number
}

interface Props {
  candles: CandleBar[]
  pair: string
  timeframe: ChartTimeframe
  livePrice?: number | null
  tradeOverlay?: TradeOverlay | null
  structure?: StructureOverlay | null
  positions?: TradePositionResponse[]
  onTimeframeChange?: (tf: ChartTimeframe) => void
  isMifxConnected?: boolean
  isWideMode?: boolean
  onToggleWideMode?: () => void
}

function snapToCandleTime(iso: string, candles: CandleBar[]): number | null {
  const ts = Math.floor(new Date(iso).getTime() / 1000)
  let result: number | null = null
  for (const c of candles) {
    if (c.time <= ts) result = c.time
    else break
  }
  return result
}

interface OhlcTooltip {
  time: string
  open: number
  high: number
  low: number
  close: number
  volume?: number | null
}

const TIMEFRAMES: ChartTimeframe[] = ['M15', 'H1', 'D1']

export function CandlestickChart({
  candles,
  pair,
  timeframe,
  livePrice,
  tradeOverlay,
  structure,
  positions = [],
  onTimeframeChange,
  isMifxConnected,
  isWideMode = false,
  onToggleWideMode,
}: Props) {
  const mainContainerRef = useRef<HTMLDivElement>(null)
  const rsiContainerRef = useRef<HTMLDivElement>(null)
  const mainChartRef = useRef<IChartApi | null>(null)
  const rsiChartRef = useRef<IChartApi | null>(null)
  const candleSeriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null)
  const ma20SeriesRef = useRef<ISeriesApi<'Line'> | null>(null)
  const ma50SeriesRef = useRef<ISeriesApi<'Line'> | null>(null)
  const volumeSeriesRef = useRef<ISeriesApi<'Histogram'> | null>(null)
  const rsiSeriesRef = useRef<ISeriesApi<'Line'> | null>(null)
  const priceLinesRef = useRef<IPriceLine[]>([])
  const markersRef = useRef<ISeriesMarkersPluginApi<Time> | null>(null)
  // Track apakah initial fit-to-content sudah dilakukan untuk dataset ini.
  // Direset saat timeframe berubah (dataset baru) supaya user tidak harus
  // manual scroll/zoom setelah ganti TF. Tapi TIDAK direset saat candle
  // update biasa — supaya zoom/pan yang sudah user atur tetap dipertahankan.
  const initialFitDoneRef = useRef(false)
  const [tooltip, setTooltip] = useState<OhlcTooltip | null>(null)
  const [isCollapsed, setIsCollapsed] = useState(false)

  // Drawing tool state — disimpan per pair+TF, load saat mount, persist saat change
  const [activeTool, setActiveTool] = useState<ToolMode>('cursor')
  const [drawings, setDrawings] = useState<Drawing[]>([])
  const [selectedDrawingId, setSelectedDrawingId] = useState<string | null>(null)
  const [snapEnabled, setSnapEnabled] = useState(false)
  // ID position box yang user sudah dismiss (kembali muncul saat ID berbeda — trade/signal baru)
  const [dismissedBoxIds, setDismissedBoxIds] = useState<Set<string>>(new Set())
  const [chartSize, setChartSize] = useState<{ width: number; height: number }>({ width: 0, height: 480 })

  // Load collapse state dari localStorage (persisted across reload)
  useEffect(() => {
    setIsCollapsed(localStorage.getItem('forexai.chartCollapsed') === 'true')
    setSnapEnabled(localStorage.getItem('forexai.snapToCandle') === 'true')
    // Load dismissed position box IDs
    try {
      const raw = localStorage.getItem('forexai.dismissedBoxes')
      if (raw) {
        const arr: string[] = JSON.parse(raw)
        if (Array.isArray(arr)) setDismissedBoxIds(new Set(arr))
      }
    } catch {/* corrupt — ignore */}
  }, [])

  const handleDismissBox = (id: string) => {
    setDismissedBoxIds((prev) => {
      const next = new Set(prev)
      next.add(id)
      try {
        localStorage.setItem('forexai.dismissedBoxes', JSON.stringify(Array.from(next)))
      } catch {/* quota — ignore */}
      return next
    })
  }

  const toggleSnap = () => {
    setSnapEnabled((prev) => {
      const next = !prev
      localStorage.setItem('forexai.snapToCandle', String(next))
      return next
    })
  }

  // Load drawings saat pair/timeframe berubah
  useEffect(() => {
    setDrawings(loadDrawings(pair, timeframe))
    setSelectedDrawingId(null)
  }, [pair, timeframe])

  // Persist drawings saat berubah
  useEffect(() => {
    saveDrawings(pair, timeframe, drawings)
  }, [pair, timeframe, drawings])

  // DEL/Backspace untuk hapus drawing yang dipilih (skip kalau locked)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.key === 'Delete' || e.key === 'Backspace') && selectedDrawingId) {
        const target = e.target as HTMLElement
        const tag = target.tagName.toLowerCase()
        if (tag === 'input' || tag === 'textarea' || target.isContentEditable) return
        setDrawings((prev) => {
          const found = prev.find((d) => d.id === selectedDrawingId)
          if (found?.locked) return prev  // locked: jangan hapus
          return prev.filter((d) => d.id !== selectedDrawingId)
        })
        setSelectedDrawingId(null)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [selectedDrawingId])

  const handleClearAllDrawings = () => {
    const unlocked = drawings.filter((d) => !d.locked)
    if (unlocked.length === 0) return
    const lockedCount = drawings.length - unlocked.length
    const msg = lockedCount > 0
      ? `Hapus ${unlocked.length} drawing? (${lockedCount} locked akan tetap ada)`
      : `Hapus semua ${unlocked.length} drawing di ${pair} ${timeframe}?`
    if (window.confirm(msg)) {
      setDrawings((prev) => prev.filter((d) => d.locked))
      setSelectedDrawingId(null)
    }
  }

  const handleDeleteSelected = () => {
    if (!selectedDrawingId) return
    const target = drawings.find((d) => d.id === selectedDrawingId)
    if (target?.locked) return  // locked: tidak boleh hapus
    setDrawings((prev) => prev.filter((d) => d.id !== selectedDrawingId))
    setSelectedDrawingId(null)
  }

  const handleToggleLockSelected = () => {
    if (!selectedDrawingId) return
    setDrawings((prev) =>
      prev.map((d) => (d.id === selectedDrawingId ? { ...d, locked: !d.locked } : d)),
    )
  }

  const handleUpdateSelectedStyle = (patch: Partial<DrawingStyle>) => {
    if (!selectedDrawingId) return
    setDrawings((prev) =>
      prev.map((d) =>
        d.id === selectedDrawingId
          ? { ...d, style: { ...d.style, ...patch } }
          : d,
      ),
    )
  }

  const selectedDrawing = selectedDrawingId
    ? drawings.find((d) => d.id === selectedDrawingId)
    : undefined

  const toggleCollapsed = () => {
    setIsCollapsed((prev) => {
      const next = !prev
      localStorage.setItem('forexai.chartCollapsed', String(next))
      return next
    })
  }

  useEffect(() => {
    if (!mainContainerRef.current || !rsiContainerRef.current) return

    const mainChart = createChart(mainContainerRef.current, {
      layout: {
        background: { type: ColorType.Solid, color: 'transparent' },
        textColor: '#9ca3af',
        fontSize: 11,
      },
      grid: {
        vertLines: { color: 'rgba(255,255,255,0.04)' },
        horzLines: { color: 'rgba(255,255,255,0.04)' },
      },
      crosshair: { mode: CrosshairMode.Normal },
      rightPriceScale: {
        borderColor: 'rgba(255,255,255,0.08)',
        scaleMargins: { top: 0.05, bottom: 0.22 },
      },
      timeScale: {
        borderColor: 'rgba(255,255,255,0.08)',
        timeVisible: true,
        secondsVisible: false,
        rightOffset: 25,  // empty area di kanan ~25 bar — supaya position box (nempel last candle) tidak ke-clip
      },
      width: mainContainerRef.current.clientWidth,
      height: 480,
    })
    setChartSize({ width: mainContainerRef.current.clientWidth, height: 480 })

    const candleSeries = mainChart.addSeries(CandlestickSeries, {
      upColor: '#22c55e',
      downColor: '#ef4444',
      borderUpColor: '#22c55e',
      borderDownColor: '#ef4444',
      wickUpColor: '#22c55e',
      wickDownColor: '#ef4444',
    })

    const ma20Series = mainChart.addSeries(LineSeries, {
      color: '#fbbf24',
      lineWidth: 2,
      priceLineVisible: false,
      lastValueVisible: false,  // hide right-side axis label — kurangi label clutter
      title: 'MA20',             // tetap muncul di legend top-left chart
    })
    const ma50Series = mainChart.addSeries(LineSeries, {
      color: '#a78bfa',
      lineWidth: 2,
      priceLineVisible: false,
      lastValueVisible: false,  // hide right-side axis label
      title: 'MA50',
    })

    const volumeSeries = mainChart.addSeries(HistogramSeries, {
      priceFormat: { type: 'volume' },
      priceScaleId: 'volume',
      color: 'rgba(148, 163, 184, 0.4)',
    })
    mainChart.priceScale('volume').applyOptions({
      scaleMargins: { top: 0.82, bottom: 0 },
    })

    const rsiChart = createChart(rsiContainerRef.current, {
      layout: {
        background: { type: ColorType.Solid, color: 'transparent' },
        textColor: '#9ca3af',
        fontSize: 10,
      },
      grid: {
        vertLines: { color: 'rgba(255,255,255,0.03)' },
        horzLines: { color: 'rgba(255,255,255,0.04)' },
      },
      crosshair: { mode: CrosshairMode.Normal },
      rightPriceScale: {
        borderColor: 'rgba(255,255,255,0.08)',
        scaleMargins: { top: 0.1, bottom: 0.1 },
      },
      timeScale: { visible: false, borderVisible: false },
      width: rsiContainerRef.current.clientWidth,
      height: 110,
    })
    const rsiSeries = rsiChart.addSeries(LineSeries, {
      color: '#60a5fa',
      lineWidth: 2,
      priceLineVisible: false,
      title: 'RSI14',
    })
    rsiSeries.createPriceLine({
      price: 70,
      color: 'rgba(239,68,68,0.5)',
      lineStyle: LineStyle.Dashed,
      lineWidth: 1,
      axisLabelVisible: true,
      title: '70',
    })
    rsiSeries.createPriceLine({
      price: 30,
      color: 'rgba(34,197,94,0.5)',
      lineStyle: LineStyle.Dashed,
      lineWidth: 1,
      axisLabelVisible: true,
      title: '30',
    })

    mainChartRef.current = mainChart
    rsiChartRef.current = rsiChart
    candleSeriesRef.current = candleSeries
    ma20SeriesRef.current = ma20Series
    ma50SeriesRef.current = ma50Series
    volumeSeriesRef.current = volumeSeries
    rsiSeriesRef.current = rsiSeries
    markersRef.current = createSeriesMarkers(candleSeries, [])

    mainChart.timeScale().subscribeVisibleLogicalRangeChange((range) => {
      if (range) rsiChart.timeScale().setVisibleLogicalRange(range)
    })
    rsiChart.timeScale().subscribeVisibleLogicalRangeChange((range) => {
      if (range) mainChart.timeScale().setVisibleLogicalRange(range)
    })

    mainChart.subscribeCrosshairMove((param) => {
      if (!param.time || !param.seriesData) {
        setTooltip(null)
        return
      }
      const candleData = param.seriesData.get(candleSeries) as CandlestickData | undefined
      if (!candleData) {
        setTooltip(null)
        return
      }
      const volData = param.seriesData.get(volumeSeries) as { value?: number } | undefined
      const dt = new Date((param.time as number) * 1000)
      setTooltip({
        time: dt.toLocaleString('id-ID', {
          day: '2-digit',
          month: 'short',
          hour: '2-digit',
          minute: '2-digit',
        }),
        open: candleData.open,
        high: candleData.high,
        low: candleData.low,
        close: candleData.close,
        volume: volData?.value,
      })
    })

    const ro = new ResizeObserver(() => {
      if (mainContainerRef.current) {
        const w = mainContainerRef.current.clientWidth
        const h = mainContainerRef.current.clientHeight || 480
        mainChart.applyOptions({ width: w })
        setChartSize({ width: w, height: h })
      }
      if (rsiContainerRef.current)
        rsiChart.applyOptions({ width: rsiContainerRef.current.clientWidth })
    })
    ro.observe(mainContainerRef.current)
    ro.observe(rsiContainerRef.current)

    return () => {
      ro.disconnect()
      mainChart.remove()
      rsiChart.remove()
      mainChartRef.current = null
      rsiChartRef.current = null
      candleSeriesRef.current = null
      ma20SeriesRef.current = null
      ma50SeriesRef.current = null
      volumeSeriesRef.current = null
      rsiSeriesRef.current = null
      priceLinesRef.current = []
    }
  }, [])

  useEffect(() => {
    if (
      !candleSeriesRef.current ||
      !ma20SeriesRef.current ||
      !ma50SeriesRef.current ||
      !volumeSeriesRef.current ||
      !rsiSeriesRef.current ||
      candles.length === 0
    )
      return

    const candleData = candles.map((c) => ({
      time: c.time as UTCTimestamp,
      open: c.open,
      high: c.high,
      low: c.low,
      close: c.close,
    }))
    candleSeriesRef.current.setData(candleData)

    const closes = candles.map((c) => c.close)
    const sma20 = computeSMA(closes, 20)
    const sma50 = computeSMA(closes, 50)
    ma20SeriesRef.current.setData(
      candles
        .map((c, i) => ({ time: c.time as UTCTimestamp, value: sma20[i] }))
        .filter((p): p is { time: UTCTimestamp; value: number } => p.value !== null),
    )
    ma50SeriesRef.current.setData(
      candles
        .map((c, i) => ({ time: c.time as UTCTimestamp, value: sma50[i] }))
        .filter((p): p is { time: UTCTimestamp; value: number } => p.value !== null),
    )

    const volumeData = candles.map((c, i) => ({
      time: c.time as UTCTimestamp,
      value: c.volume ?? 0,
      color:
        i > 0 && c.close >= candles[i - 1].close
          ? 'rgba(34,197,94,0.45)'
          : 'rgba(239,68,68,0.45)',
    }))
    volumeSeriesRef.current.setData(volumeData)

    const rsi = computeRSI(closes, 14)
    rsiSeriesRef.current.setData(
      candles
        .map((c, i) => ({ time: c.time as UTCTimestamp, value: rsi[i] }))
        .filter((p): p is { time: UTCTimestamp; value: number } => p.value !== null),
    )

    // Hanya fit-to-content saat pertama kali data masuk untuk dataset ini.
    // Pada update berikutnya (live candle tick), biarkan zoom/pan user
    // tetap apa adanya — tidak boleh snap back ke posisi awal.
    if (!initialFitDoneRef.current) {
      mainChartRef.current?.timeScale().fitContent()
      initialFitDoneRef.current = true
    }
  }, [candles])

  // Reset fit-state saat ganti timeframe — dataset berbeda, perlu fit ulang.
  useEffect(() => {
    initialFitDoneRef.current = false
  }, [timeframe])

  // Saat re-expand dari collapsed: sesuaikan width container — TIDAK pakai
  // fitContent supaya zoom/pan user dipertahankan.
  useEffect(() => {
    if (isCollapsed) return
    requestAnimationFrame(() => {
      if (mainContainerRef.current && mainChartRef.current) {
        mainChartRef.current.applyOptions({ width: mainContainerRef.current.clientWidth })
      }
      if (rsiContainerRef.current && rsiChartRef.current) {
        rsiChartRef.current.applyOptions({ width: rsiContainerRef.current.clientWidth })
      }
    })
  }, [isCollapsed])

  useEffect(() => {
    if (!candleSeriesRef.current || livePrice == null || candles.length === 0) return
    const last = candles[candles.length - 1]
    candleSeriesRef.current.update({
      time: last.time as UTCTimestamp,
      open: last.open,
      high: Math.max(last.high, livePrice),
      low: Math.min(last.low, livePrice),
      close: livePrice,
    })
  }, [livePrice, candles])

  useEffect(() => {
    if (!candleSeriesRef.current) return
    priceLinesRef.current.forEach((pl) => candleSeriesRef.current!.removePriceLine(pl))
    priceLinesRef.current = []

    const added: IPriceLine[] = []

    // tradeOverlay (entry/SL/TP) dirender oleh PositionBoxOverlay (TradingView-style),
    // bukan priceLine biasa lagi.

    if (structure) {
      added.push(
        candleSeriesRef.current.createPriceLine({
          price: structure.nearestSupport,
          color: 'rgba(34,197,94,0.5)',
          lineStyle: LineStyle.Dotted,
          lineWidth: 1,
          axisLabelVisible: false,  // hide right-side label — kurangi clutter
          title: 'Support',
        }),
      )
      added.push(
        candleSeriesRef.current.createPriceLine({
          price: structure.nearestResistance,
          color: 'rgba(239,68,68,0.5)',
          lineStyle: LineStyle.Dotted,
          lineWidth: 1,
          axisLabelVisible: false,  // hide right-side label
          title: 'Resistance',
        }),
      )
    }

    priceLinesRef.current = added
  }, [structure])

  // Markers untuk trade history (entry arrow + exit win/loss square)
  useEffect(() => {
    if (!markersRef.current || candles.length === 0) return

    const markers: SeriesMarker<Time>[] = []
    positions.forEach((pos) => {
      if (pos.status === 'SKIPPED' || !pos.openedAt) return
      if (pos.direction !== 'BUY' && pos.direction !== 'SELL') return

      const entryTime = snapToCandleTime(pos.openedAt, candles)
      if (entryTime !== null) {
        markers.push({
          time: entryTime as Time,
          position: pos.direction === 'BUY' ? 'belowBar' : 'aboveBar',
          color: pos.direction === 'BUY' ? '#3b82f6' : '#f97316',
          shape: pos.direction === 'BUY' ? 'arrowUp' : 'arrowDown',
          text: `${pos.direction[0]} ${pos.lotSize.toFixed(2)}`,
        })
      }

      if (pos.closedAt && pos.status !== 'ACTIVE') {
        const exitTime = snapToCandleTime(pos.closedAt, candles)
        if (exitTime !== null) {
          const isWin = pos.status === 'CLOSED_WIN'
          const sign = pos.floatingPnlPips >= 0 ? '+' : ''
          markers.push({
            time: exitTime as Time,
            position: isWin ? 'aboveBar' : 'belowBar',
            color: isWin ? '#22c55e' : '#ef4444',
            shape: 'square',
            text: `${sign}${pos.floatingPnlPips}p`,
          })
        }
      }
    })

    markers.sort((a, b) => (a.time as number) - (b.time as number))
    markersRef.current.setMarkers(markers)
  }, [positions, candles])

  const lastCandle = candles[candles.length - 1]
  const prevCandle = candles[candles.length - 2]
  const displayPrice = livePrice ?? lastCandle?.close
  const change = useMemo(() => {
    if (!displayPrice || !prevCandle) return null
    return ((displayPrice - prevCandle.close) / prevCandle.close) * 100
  }, [displayPrice, prevCandle])

  // Derive position boxes dari tradeOverlay untuk PositionBoxOverlay.
  // Skip box yang ID-nya sudah di-dismiss user (id berbeda = trade/signal baru = muncul lagi).
  // anchorTime: active pakai openedAt (dari page.tsx), pending fallback ke last candle time
  // sehingga box nempel ke candle terakhir dan ikut bergerak saat user pan/zoom.
  const positionBoxes = useMemo<PositionBox[]>(() => {
    if (!tradeOverlay) return []
    const id = tradeOverlay.id ?? `fallback-${tradeOverlay.direction}-${tradeOverlay.entry}-${tradeOverlay.stopLoss}`
    if (dismissedBoxIds.has(id)) return []
    const lastCandleTime = candles[candles.length - 1]?.time
    const anchorTime = tradeOverlay.anchorTime ?? lastCandleTime
    if (anchorTime === undefined) return []  // no candles yet, can't anchor
    return [{
      id,
      entry:            tradeOverlay.entry,
      stopLoss:         tradeOverlay.stopLoss,
      takeProfit:       tradeOverlay.takeProfit,
      direction:        tradeOverlay.direction,
      status:           tradeOverlay.status ?? 'pending',
      livePnlPips:      tradeOverlay.livePnlPips,
      livePnlUsd:       tradeOverlay.livePnlUsd,
      lotSize:          tradeOverlay.lotSize,
      riskAmount:       tradeOverlay.riskAmount,
      potentialProfit:  tradeOverlay.potentialProfit,
      riskReward:       tradeOverlay.riskReward,
      confidence:       tradeOverlay.confidence,
      anchorTime,
    }]
  }, [tradeOverlay, dismissedBoxIds, candles])

  return (
    <Card className="bg-card/50">
      <CardHeader className="pb-2 pt-3 px-4">
        <div className="flex items-center justify-between gap-2 flex-wrap">
          <div className="flex items-center gap-3">
            <span className="text-sm font-medium">{pair}</span>
            <div className="flex gap-1">
              {TIMEFRAMES.map((tf) => (
                <button
                  key={tf}
                  onClick={() => onTimeframeChange?.(tf)}
                  className={cn(
                    'px-2 py-0.5 text-xs font-mono rounded border transition-colors',
                    tf === timeframe
                      ? 'bg-primary/15 border-primary/40 text-primary'
                      : 'border-border/40 text-muted-foreground hover:text-foreground hover:border-border',
                  )}
                >
                  {tf}
                </button>
              ))}
            </div>
            {isMifxConnected && (
              <span className="relative flex h-2 w-2" title="MIFX live">
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
                <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
              </span>
            )}
            {!isCollapsed && (
              <div className="border-l border-border/40 pl-3 ml-1">
                <ChartToolbar
                  activeTool={activeTool}
                  onSelectTool={setActiveTool}
                  onClearAll={handleClearAllDrawings}
                  onDeleteSelected={handleDeleteSelected}
                  selectedId={selectedDrawingId}
                  selectedStyle={selectedDrawing?.style}
                  selectedLocked={selectedDrawing?.locked}
                  onUpdateSelectedStyle={handleUpdateSelectedStyle}
                  onToggleLockSelected={handleToggleLockSelected}
                  drawingCount={drawings.length}
                  snapEnabled={snapEnabled}
                  onToggleSnap={toggleSnap}
                />
              </div>
            )}
          </div>
          <div className="flex items-center gap-3 text-xs">
            {onToggleWideMode && !isCollapsed && (
              <button
                onClick={onToggleWideMode}
                className="p-1 rounded hover:bg-muted/60 text-muted-foreground hover:text-foreground transition-colors"
                title={isWideMode ? 'Mode normal (chart + sidebar)' : 'Mode lebar (chart full-width, sidebar pindah bawah)'}
              >
                {isWideMode ? <Minimize2 className="h-3.5 w-3.5" /> : <Maximize2 className="h-3.5 w-3.5" />}
              </button>
            )}
            <button
              onClick={toggleCollapsed}
              className="p-1 rounded hover:bg-muted/60 text-muted-foreground hover:text-foreground transition-colors"
              title={isCollapsed ? 'Expand chart' : 'Minimize chart'}
            >
              {isCollapsed ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronUp className="h-3.5 w-3.5" />}
            </button>
            {displayPrice && (
              <span className="font-mono font-semibold text-base">
                {displayPrice.toFixed(5)}
              </span>
            )}
            {change !== null && (
              <span className={change >= 0 ? 'text-green-500' : 'text-red-500'}>
                {change >= 0 ? '+' : ''}
                {change.toFixed(2)}%
              </span>
            )}
          </div>
        </div>
      </CardHeader>
      <CardContent
        className="p-0 pb-2 relative"
        style={isCollapsed ? { display: 'none' } : undefined}
      >
        <div ref={mainContainerRef} className="w-full relative" style={{ minHeight: 480 }}>
          <PositionBoxOverlay
            chart={mainChartRef.current}
            series={candleSeriesRef.current}
            boxes={positionBoxes}
            width={chartSize.width}
            height={chartSize.height}
            onDismiss={handleDismissBox}
          />
          <ChartDrawingOverlay
            chart={mainChartRef.current}
            series={candleSeriesRef.current}
            activeTool={activeTool}
            drawings={drawings}
            onDrawingsChange={setDrawings}
            selectedId={selectedDrawingId}
            onSelectedIdChange={setSelectedDrawingId}
            width={chartSize.width}
            height={chartSize.height}
            candles={candles}
            snapEnabled={snapEnabled}
          />
        </div>
        {candles.length === 0 && (
          <div className="absolute inset-0 flex items-center justify-center text-sm text-muted-foreground pointer-events-none">
            <div className="text-center space-y-1">
              <p>Menunggu candle dari MIFX EA…</p>
              <p className="text-xs">
                Pastikan EA v1.20 berjalan di MT5 (klik <strong>Update EA</strong> jika belum).
              </p>
            </div>
          </div>
        )}
        {tooltip && (
          <div className="absolute top-2 left-2 z-10 rounded-md bg-background/85 backdrop-blur border border-border/60 px-2.5 py-1.5 text-[10px] font-mono space-y-0.5 pointer-events-none">
            <div className="text-muted-foreground">{tooltip.time}</div>
            <div className="flex gap-2">
              <span>
                <span className="text-muted-foreground">O</span>{' '}
                <span className="font-semibold">{tooltip.open.toFixed(5)}</span>
              </span>
              <span>
                <span className="text-muted-foreground">H</span>{' '}
                <span className="font-semibold">{tooltip.high.toFixed(5)}</span>
              </span>
              <span>
                <span className="text-muted-foreground">L</span>{' '}
                <span className="font-semibold">{tooltip.low.toFixed(5)}</span>
              </span>
              <span>
                <span className="text-muted-foreground">C</span>{' '}
                <span className="font-semibold">{tooltip.close.toFixed(5)}</span>
              </span>
            </div>
            {tooltip.volume != null && tooltip.volume > 0 && (
              <div>
                <span className="text-muted-foreground">Vol</span>{' '}
                <span className="font-semibold">{tooltip.volume.toLocaleString('id-ID')}</span>
              </div>
            )}
          </div>
        )}
        <div ref={rsiContainerRef} className="w-full" style={{ minHeight: 110 }} />
      </CardContent>
    </Card>
  )
}
