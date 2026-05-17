'use client'

import { useCallback, useEffect, useRef } from 'react'
import type { IChartApi, ISeriesApi, UTCTimestamp } from 'lightweight-charts'

export interface PositionBox {
  entry: number
  stopLoss: number
  takeProfit: number
  direction: 'BUY' | 'SELL'
  /** active = posisi sudah open; pending = signal yang belum di-execute */
  status: 'active' | 'pending'
  lotSize?: number
  riskAmount?: number       // $ amount at risk (lot × pip distance × pip value)
  potentialProfit?: number  // $ potential profit at TP
  riskReward?: number       // RR ratio (potentialProfit / riskAmount)
  livePnlPips?: number
  livePnlUsd?: number
  /** Optional anchor time supaya box mulai dari titik open. Kalau null, mulai dari ~65% chart. */
  anchorTime?: number
  /** Optional confidence label di tengah untuk pending signal (0-100). */
  confidence?: number
}

interface Props {
  chart: IChartApi | null
  series: ISeriesApi<'Candlestick'> | null
  boxes: PositionBox[]
  width: number
  height: number
}

/**
 * Render TradingView-style "long/short position" tool — box red/green dengan
 * labels pip/%/$/RR. Read-only (interactive drag-edit di future stage).
 *
 * Layer di antara chart canvas dan drawing overlay (z-index 3, drawing 5).
 * Subscribe ke visible range change supaya box auto-redraw saat zoom/pan.
 */
export function PositionBoxOverlay({ chart, series, boxes, width, height }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)

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

    for (const box of boxes) {
      drawPositionBox(ctx, box, chart, series, width)
    }
  }, [chart, series, boxes, width, height])

  // Redraw saat zoom/pan
  useEffect(() => {
    if (!chart) return
    const ts = chart.timeScale()
    const handler = () => render()
    ts.subscribeVisibleLogicalRangeChange(handler)
    return () => ts.unsubscribeVisibleLogicalRangeChange(handler)
  }, [chart, render])

  useEffect(() => {
    render()
  }, [render])

  return (
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
  )
}

function drawPositionBox(
  ctx: CanvasRenderingContext2D,
  box: PositionBox,
  chart: IChartApi,
  series: ISeriesApi<'Candlestick'>,
  canvasWidth: number,
) {
  const entryY = series.priceToCoordinate(box.entry)
  const slY = series.priceToCoordinate(box.stopLoss)
  const tpY = series.priceToCoordinate(box.takeProfit)
  if (entryY === null || slY === null || tpY === null) return

  // X start: dari anchorTime kalau ada, else 65% chart width
  let xStart: number
  if (box.anchorTime !== undefined) {
    const coord = chart.timeScale().timeToCoordinate(box.anchorTime as UTCTimestamp)
    xStart = coord !== null ? coord : canvasWidth * 0.65
  } else {
    xStart = canvasWidth * 0.65
  }
  // Pastikan box punya lebar minimum
  if (xStart > canvasWidth - 100) xStart = canvasWidth - 200
  if (xStart < 0) xStart = 0
  const xEnd = canvasWidth - 8

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

  // ── Labels ────────────────────────────────────────────────────────────
  ctx.font = '10px ui-monospace, SFMono-Regular, monospace'
  ctx.textBaseline = 'middle'

  const slPips = Math.round(Math.abs(box.stopLoss - box.entry) * 10000)
  const tpPips = Math.round(Math.abs(box.takeProfit - box.entry) * 10000)
  const slPct = Math.abs(box.stopLoss - box.entry) / box.entry * 100
  const tpPct = Math.abs(box.takeProfit - box.entry) / box.entry * 100

  // SL label (di sisi SL — atas kalau SELL/SL above entry, bawah kalau BUY)
  drawPillLabel(
    ctx,
    `Stop: ${slPips}p (${slPct.toFixed(2)}%)${box.riskAmount ? ` · $${box.riskAmount.toFixed(2)}` : ''}`,
    xEnd - 4,
    slY,
    '#ef4444',
    'right',
    pending ? 0.7 : 1,
  )

  // TP label
  drawPillLabel(
    ctx,
    `Target: ${tpPips}p (${tpPct.toFixed(2)}%)${box.potentialProfit ? ` · $${box.potentialProfit.toFixed(2)}` : ''}`,
    xEnd - 4,
    tpY,
    '#22c55e',
    'right',
    pending ? 0.7 : 1,
  )

  // Entry label (di tengah, kanan, dengan P&L + RR + status)
  const entryLabelParts: string[] = []
  if (pending) {
    entryLabelParts.push('SETUP')
    if (box.confidence !== undefined) entryLabelParts.push(`${box.confidence}%`)
  } else {
    if (box.livePnlUsd !== undefined && box.livePnlPips !== undefined) {
      const sign = box.livePnlUsd >= 0 ? '+' : ''
      entryLabelParts.push(`${sign}$${box.livePnlUsd.toFixed(2)} (${sign}${box.livePnlPips}p)`)
    }
  }
  if (box.lotSize) entryLabelParts.push(`${box.lotSize.toFixed(2)} lot`)
  if (box.riskReward) entryLabelParts.push(`RR 1:${box.riskReward.toFixed(2)}`)
  const entryLabel = `${box.direction} ${box.entry.toFixed(5)} · ${entryLabelParts.join(' · ')}`

  drawPillLabel(
    ctx,
    entryLabel,
    xStart + 6,
    entryY,
    entryColor,
    'left',
    pending ? 0.7 : 1,
  )
}

function drawPillLabel(
  ctx: CanvasRenderingContext2D,
  text: string,
  x: number,
  y: number,
  color: string,
  align: 'left' | 'right',
  opacity: number,
) {
  ctx.font = '10px ui-monospace, SFMono-Regular, monospace'
  ctx.textBaseline = 'middle'
  const metrics = ctx.measureText(text)
  const padX = 6
  const padY = 4
  const w = metrics.width + padX * 2
  const h = 16 + padY
  const rectX = align === 'left' ? x : x - w
  // Background pill (dark, untuk readability di chart)
  ctx.fillStyle = `rgba(15, 23, 42, ${0.92 * opacity})`
  roundedRect(ctx, rectX, y - h / 2, w, h, 4)
  ctx.fill()
  // Border tipis warna sesuai
  ctx.strokeStyle = color + Math.round(255 * opacity).toString(16).padStart(2, '0')
  ctx.lineWidth = 1
  roundedRect(ctx, rectX, y - h / 2, w, h, 4)
  ctx.stroke()
  // Text
  ctx.fillStyle = color
  ctx.textAlign = align === 'left' ? 'left' : 'right'
  ctx.fillText(text, align === 'left' ? rectX + padX : rectX + w - padX, y)
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
