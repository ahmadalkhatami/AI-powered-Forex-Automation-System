'use client'

import { useCallback, useEffect, useRef, useState } from 'react'
import { X } from 'lucide-react'
import type { IChartApi, ISeriesApi, UTCTimestamp } from 'lightweight-charts'

export interface PositionBox {
  id: string
  entry: number
  stopLoss: number
  takeProfit: number
  direction: 'BUY' | 'SELL'
  /** active = posisi sudah open; pending = signal yang belum di-execute */
  status: 'active' | 'pending'
  lotSize?: number
  riskAmount?: number       // $ amount at risk
  potentialProfit?: number  // $ potential profit at TP
  riskReward?: number       // RR ratio
  livePnlPips?: number
  livePnlUsd?: number
  /** Confidence 0-100 untuk pending setup label. */
  confidence?: number
  /** Unix timestamp (s). Box anchor di sini — saat pan/zoom, box ikut bergerak dengan candle.
   *  Active: openedAt. Pending: last-candle time (nempel ke current bar). */
  anchorTime: number
}

interface Props {
  chart: IChartApi | null
  series: ISeriesApi<'Candlestick'> | null
  boxes: PositionBox[]
  width: number
  height: number
  onDismiss?: (id: string) => void
}

// Box width fixed di pixel, posisi LEFT edge anchored ke time (candle).
// Saat user pan/zoom chart, box bergerak ikut candle — bukan fixed di viewport.
const PRICE_SCALE_PAD = 60   // hindari overlap dengan price scale axis labels
const BOX_WIDTH = 280
const CLOSE_BTN_SIZE = 18

interface BoxLayout {
  id: string
  /** Pixel x range box */
  xStart: number
  xEnd: number
  /** Pixel y untuk SL/TP/Entry (sebelum collision adjust) */
  slY: number
  tpY: number
  entryY: number
  /** Pixel y label setelah collision adjust */
  slLabelY: number
  tpLabelY: number
  entryLabelY: number
  /** Top edge box untuk posisi close button */
  topY: number
}

/**
 * Render TradingView-style "long/short position" tool — fixed-width box
 * di right edge chart (nempel area candle aktif), labels centered di
 * dalam box width supaya tidak overlap dengan price scale axis labels.
 *
 * Read-only canvas + HTML close button (×) per box untuk manual dismiss.
 */
export function PositionBoxOverlay({ chart, series, boxes, width, height, onDismiss }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [layouts, setLayouts] = useState<BoxLayout[]>([])
  // renderTick force re-compute layouts saat chart pan/zoom (priceToCoordinate berubah)
  const [renderTick, setRenderTick] = useState(0)

  const computeLayouts = useCallback((): BoxLayout[] => {
    if (!chart || !series) return []
    const result: BoxLayout[] = []
    for (const box of boxes) {
      const entryYRaw = series.priceToCoordinate(box.entry)
      const slYRaw    = series.priceToCoordinate(box.stopLoss)
      const tpYRaw    = series.priceToCoordinate(box.takeProfit)
      if (entryYRaw === null || slYRaw === null || tpYRaw === null) continue
      // Coordinate is branded number — unwrap ke plain number untuk arithmetic
      const entryY = entryYRaw as number
      const slY    = slYRaw    as number
      const tpY    = tpYRaw    as number

      // xStart anchor ke anchorTime → box ikut bergerak saat pan/zoom chart.
      // Kalau anchorTime di luar visible range, SKIP box ini (ikut hilang dengan candle).
      const anchorPxRaw = chart.timeScale().timeToCoordinate(box.anchorTime as UTCTimestamp)
      if (anchorPxRaw === null) continue
      let xStart = anchorPxRaw as number
      // Box harus tetap visible — clamp ke viewport bounds tapi jangan offset terlalu jauh
      // supaya posisi tetap "nempel" dengan candle yang sedang aktif
      const maxXStart = width - PRICE_SCALE_PAD - BOX_WIDTH
      if (xStart > maxXStart) xStart = maxXStart
      if (xStart < 0) xStart = 0
      const xEnd = xStart + BOX_WIDTH

      // Label positioning + collision resolver: jaga gap minimum antar label.
      // Setiap label tinggi ~20px (10px font + 4px pad atas/bawah + border).
      // Outside offset awal 12px, lalu kalau masih overlap di-push lagi.
      const LABEL_HEIGHT = 20
      const MIN_GAP = 4
      const baseOffset = 12

      // Tentukan arah label berdasarkan posisi SL/TP relatif terhadap Entry.
      const slAbove = slY < entryY
      const tpAbove = tpY < entryY
      let slLabelY: number = slAbove ? slY - baseOffset : slY + baseOffset
      let tpLabelY: number = tpAbove ? tpY - baseOffset : tpY + baseOffset
      const entryLabelY: number = entryY

      // Collision resolver: untuk setiap pasang label yang berdekatan,
      // push label "luar" menjauh sampai gap >= LABEL_HEIGHT + MIN_GAP.
      const minDist = LABEL_HEIGHT + MIN_GAP
      // Entry ↔ SL
      if (Math.abs(slLabelY - entryLabelY) < minDist) {
        slLabelY = slAbove ? entryLabelY - minDist : entryLabelY + minDist
      }
      // Entry ↔ TP
      if (Math.abs(tpLabelY - entryLabelY) < minDist) {
        tpLabelY = tpAbove ? entryLabelY - minDist : entryLabelY + minDist
      }
      // Kalau SL dan TP di sisi yang sama dari entry (jarang tapi mungkin di pending preview)
      if (slAbove === tpAbove && Math.abs(slLabelY - tpLabelY) < minDist) {
        if (slAbove) {
          // Keduanya di atas — yang lebih jauh dari entry di-push lebih jauh lagi
          if (slY <= tpY) slLabelY = tpLabelY - minDist
          else tpLabelY = slLabelY - minDist
        } else {
          if (slY >= tpY) slLabelY = tpLabelY + minDist
          else tpLabelY = slLabelY + minDist
        }
      }

      // Clamp supaya label tidak keluar chart vertical bounds
      const clamp = (v: number) => Math.max(12, Math.min(height - 12, v))
      slLabelY = clamp(slLabelY)
      tpLabelY = clamp(tpLabelY)

      result.push({
        id: box.id,
        xStart, xEnd,
        slY, tpY, entryY,
        slLabelY, tpLabelY,
        entryLabelY,
        topY: Math.min(slY, tpY),
      })
    }
    return result
  }, [boxes, chart, series, width, height])

  const render = useCallback(() => {
    const canvas = canvasRef.current
    if (!canvas || !chart || !series) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    const dpr = window.devicePixelRatio || 1
    if (canvas.width !== width * dpr || canvas.height !== height * dpr) {
      canvas.width = width * dpr
      canvas.height = height * dpr
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    ctx.clearRect(0, 0, width, height)

    const newLayouts = computeLayouts()
    for (let i = 0; i < boxes.length && i < newLayouts.length; i++) {
      drawPositionBox(ctx, boxes[i], newLayouts[i])
    }
    setLayouts(newLayouts)
  }, [boxes, chart, series, width, height, computeLayouts])

  // Redraw saat zoom/pan
  useEffect(() => {
    if (!chart) return
    const ts = chart.timeScale()
    const handler = () => {
      render()
      setRenderTick((t) => t + 1)
    }
    ts.subscribeVisibleLogicalRangeChange(handler)
    return () => ts.unsubscribeVisibleLogicalRangeChange(handler)
  }, [chart, render])

  useEffect(() => {
    render()
  }, [render])

  return (
    <>
      <canvas
        ref={canvasRef}
        style={{
          position: 'absolute',
          top: 0,
          left: 0,
          width,
          height,
          pointerEvents: 'none',
          zIndex: 3,
        }}
      />
      {onDismiss && layouts.map((layout) => (
        <button
          key={layout.id + '-close-' + renderTick}
          onClick={() => onDismiss(layout.id)}
          title="Tutup position box"
          style={{
            position: 'absolute',
            left: layout.xEnd - CLOSE_BTN_SIZE - 4,
            top: layout.topY + 4,
            width: CLOSE_BTN_SIZE,
            height: CLOSE_BTN_SIZE,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'rgba(15, 23, 42, 0.85)',
            color: '#94a3b8',
            border: '1px solid rgba(255,255,255,0.15)',
            borderRadius: 4,
            cursor: 'pointer',
            padding: 0,
            pointerEvents: 'auto',
            zIndex: 4,
          }}
          onMouseEnter={(e) => {
            e.currentTarget.style.color = '#ef4444'
            e.currentTarget.style.borderColor = 'rgba(239, 68, 68, 0.5)'
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.color = '#94a3b8'
            e.currentTarget.style.borderColor = 'rgba(255,255,255,0.15)'
          }}
        >
          <X size={12} strokeWidth={2.5} />
        </button>
      ))}
    </>
  )
}

function drawPositionBox(
  ctx: CanvasRenderingContext2D,
  box: PositionBox,
  layout: BoxLayout,
) {
  const { xStart, xEnd, slY, tpY, entryY, slLabelY, tpLabelY, entryLabelY } = layout
  const pending = box.status === 'pending'
  const opacity = pending ? 0.55 : 1
  const dash: number[] = pending ? [5, 4] : []

  // ── Red zone (Entry ↔ SL) ─────────────────────────────────────────────
  ctx.fillStyle = `rgba(239, 68, 68, ${0.12 * opacity})`
  ctx.fillRect(xStart, Math.min(entryY, slY), xEnd - xStart, Math.abs(slY - entryY))
  ctx.strokeStyle = `rgba(239, 68, 68, ${0.7 * opacity})`
  ctx.lineWidth = 1
  ctx.setLineDash(dash)
  ctx.strokeRect(xStart, Math.min(entryY, slY), xEnd - xStart, Math.abs(slY - entryY))

  // ── Green zone (Entry ↔ TP) ───────────────────────────────────────────
  ctx.fillStyle = `rgba(34, 197, 94, ${0.12 * opacity})`
  ctx.fillRect(xStart, Math.min(entryY, tpY), xEnd - xStart, Math.abs(tpY - entryY))
  ctx.strokeStyle = `rgba(34, 197, 94, ${0.7 * opacity})`
  ctx.strokeRect(xStart, Math.min(entryY, tpY), xEnd - xStart, Math.abs(tpY - entryY))

  // ── Entry line (solid) ────────────────────────────────────────────────
  ctx.setLineDash([])
  const entryColor = box.direction === 'BUY' ? '#3b82f6' : '#f97316'
  ctx.strokeStyle = entryColor + (pending ? 'aa' : 'ff')
  ctx.lineWidth = 1.5
  ctx.beginPath()
  ctx.moveTo(xStart, entryY)
  ctx.lineTo(xEnd, entryY)
  ctx.stroke()

  // ── Labels (left-aligned, compact, di dalam box width) ────────────────
  // Format kompak supaya tidak overflow box dan tidak overlap dengan price scale.
  ctx.font = '10px ui-monospace, SFMono-Regular, monospace'
  ctx.textBaseline = 'middle'

  const slPips = Math.round(Math.abs(box.stopLoss - box.entry) * 10000)
  const tpPips = Math.round(Math.abs(box.takeProfit - box.entry) * 10000)
  const labelX = xStart + 6  // small padding dari left edge box
  const maxLabelWidth = xEnd - xStart - 12

  // SL label — compact: "SL 15p · $9.92"
  const slLabel = box.riskAmount
    ? `SL ${slPips}p · $${box.riskAmount.toFixed(2)}`
    : `SL ${slPips}p`
  drawPillLabelLeft(ctx, slLabel, labelX, slLabelY, '#ef4444', opacity, maxLabelWidth)

  // TP label — compact: "TP 22p · $15.40"
  const tpLabel = box.potentialProfit
    ? `TP ${tpPips}p · $${box.potentialProfit.toFixed(2)}`
    : `TP ${tpPips}p`
  drawPillLabelLeft(ctx, tpLabel, labelX, tpLabelY, '#22c55e', opacity, maxLabelWidth)

  // Entry label — compact: drop entry price (sudah ada di entry line), lot, RR
  const entryParts: string[] = [box.direction]
  if (pending) {
    if (box.confidence !== undefined) entryParts.push(`${box.confidence}%`)
  } else {
    if (box.livePnlUsd !== undefined && box.livePnlPips !== undefined) {
      const sign = box.livePnlUsd >= 0 ? '+' : ''
      entryParts.push(`${sign}$${box.livePnlUsd.toFixed(2)}`)
    }
  }
  if (box.lotSize) entryParts.push(`${box.lotSize.toFixed(2)}L`)
  if (box.riskReward) entryParts.push(`1:${box.riskReward.toFixed(2)}`)
  const entryLabel = entryParts.join(' · ')

  drawPillLabelLeft(ctx, entryLabel, labelX, entryLabelY, entryColor, opacity, maxLabelWidth)
}

function drawPillLabelLeft(
  ctx: CanvasRenderingContext2D,
  text: string,
  leftX: number,
  y: number,
  color: string,
  opacity: number,
  maxWidth: number,
) {
  ctx.font = '10px ui-monospace, SFMono-Regular, monospace'
  ctx.textBaseline = 'middle'
  // Truncate text supaya fit dalam maxWidth
  let displayText = text
  let metrics = ctx.measureText(displayText)
  const padX = 6
  const padY = 4
  while (metrics.width + padX * 2 > maxWidth && displayText.length > 4) {
    displayText = displayText.slice(0, -2) + '…'
    metrics = ctx.measureText(displayText)
  }
  const w = metrics.width + padX * 2
  const h = 16 + padY
  // Background pill (dark untuk readability)
  ctx.fillStyle = `rgba(15, 23, 42, ${0.92 * opacity})`
  roundedRect(ctx, leftX, y - h / 2, w, h, 4)
  ctx.fill()
  // Border tipis
  ctx.strokeStyle = color + Math.round(255 * opacity).toString(16).padStart(2, '0')
  ctx.lineWidth = 1
  roundedRect(ctx, leftX, y - h / 2, w, h, 4)
  ctx.stroke()
  // Text
  ctx.fillStyle = color
  ctx.textAlign = 'left'
  ctx.fillText(displayText, leftX + padX, y)
}

function roundedRect(
  ctx: CanvasRenderingContext2D,
  x: number, y: number, w: number, h: number, r: number,
) {
  ctx.beginPath()
  ctx.moveTo(x + r, y)
  ctx.lineTo(x + w - r, y)
  ctx.quadraticCurveTo(x + w, y, x + w, y + r)
  ctx.lineTo(x + w, y + h - r)
  ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h)
  ctx.lineTo(x + r, y + h)
  ctx.quadraticCurveTo(x, y + h, x, y + h - r)
  ctx.lineTo(x, y + r)
  ctx.quadraticCurveTo(x, y, x + r, y)
  ctx.closePath()
}
