'use client'

import { useCallback, useEffect, useRef } from 'react'
import type { IChartApi, ISeriesApi, UTCTimestamp } from 'lightweight-charts'
import type { CandleBar, TimeframePattern } from '@/lib/types'

interface Props {
  chart: IChartApi | null
  series: ISeriesApi<'Candlestick'> | null
  candles: CandleBar[]
  pattern: TimeframePattern | null
  width: number
  height: number
}

/**
 * TradingView-style pattern highlight overlay.
 * - Highlight rectangle around candles yang form pattern (light fill, border bias-color)
 * - Label pill di atas/bawah highlight: pattern name + reliability
 * - Anchor ke candle time, ikut bergerak saat pan/zoom (subscribe range change)
 * - Read-only: pointer-events:none
 */
export function PatternHighlightOverlay({ chart, series, candles, pattern, width, height }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  const render = useCallback(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    const dpr = window.devicePixelRatio || 1
    if (canvas.width !== width * dpr || canvas.height !== height * dpr) {
      canvas.width = width * dpr
      canvas.height = height * dpr
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    ctx.clearRect(0, 0, width, height)

    if (!chart || !series || !pattern || pattern.name === 'None' || pattern.candleTimes.length === 0) {
      return
    }

    // Locate candles dari time match
    const candleSet = new Set(pattern.candleTimes)
    const matched = candles.filter((c) => candleSet.has(c.time as number))
    if (matched.length === 0) return

    // Compute bounding box: time range + price range (min/max of matched candles)
    let minPx = Infinity, maxPx = -Infinity
    let minPrice = Infinity, maxPrice = -Infinity
    for (const c of matched) {
      const x = chart.timeScale().timeToCoordinate(c.time as UTCTimestamp) as number | null
      if (x === null) continue
      if (x < minPx) minPx = x
      if (x > maxPx) maxPx = x
      if (c.low < minPrice) minPrice = c.low
      if (c.high > maxPrice) maxPrice = c.high
    }
    if (minPx === Infinity) return

    const yHigh = series.priceToCoordinate(maxPrice) as number | null
    const yLow  = series.priceToCoordinate(minPrice) as number | null
    if (yHigh === null || yLow === null) return

    // Approximate candle width: derive from 2-candle spacing
    const lastCandle = candles[candles.length - 1]
    const prevCandle = candles[candles.length - 2]
    let approxBarWidth = 8
    if (lastCandle && prevCandle) {
      const x1 = chart.timeScale().timeToCoordinate(lastCandle.time as UTCTimestamp) as number | null
      const x2 = chart.timeScale().timeToCoordinate(prevCandle.time as UTCTimestamp) as number | null
      if (x1 !== null && x2 !== null) {
        approxBarWidth = Math.abs(x1 - x2)
      }
    }

    // Padding box: half barWidth tiap sisi, 4px vertical
    const padX = approxBarWidth * 0.6
    const padY = 6
    const x0 = minPx - padX
    const x1 = maxPx + padX
    const y0 = yHigh - padY
    const y1 = yLow + padY

    const color =
      pattern.bias === 'Bullish' ? { stroke: 'rgba(16, 185, 129, 0.9)', fill: 'rgba(16, 185, 129, 0.10)', text: '#10b981' } :
      pattern.bias === 'Bearish' ? { stroke: 'rgba(239, 68, 68, 0.9)',  fill: 'rgba(239, 68, 68, 0.10)',  text: '#ef4444' } :
      { stroke: 'rgba(245, 158, 11, 0.9)', fill: 'rgba(245, 158, 11, 0.10)', text: '#f59e0b' }

    // Highlight rect (fill + dashed border)
    ctx.fillStyle = color.fill
    ctx.fillRect(x0, y0, x1 - x0, y1 - y0)
    ctx.strokeStyle = color.stroke
    ctx.lineWidth = 1.2
    ctx.setLineDash([4, 3])
    ctx.strokeRect(x0, y0, x1 - x0, y1 - y0)
    ctx.setLineDash([])

    // Label pill: pattern name above highlight (Bullish) atau below (Bearish/Neutral)
    const labelText = `${pattern.name} · ${(pattern.reliability * 100).toFixed(0)}%`
    ctx.font = '11px ui-monospace, SFMono-Regular, monospace'
    ctx.textBaseline = 'middle'
    const metrics = ctx.measureText(labelText)
    const padXLabel = 6, padYLabel = 3
    const labelW = metrics.width + padXLabel * 2
    const labelH = 16 + padYLabel * 2

    const placeAbove = pattern.bias === 'Bullish'
    const labelCenterX = (x0 + x1) / 2
    const labelY = placeAbove ? y0 - labelH / 2 - 4 : y1 + labelH / 2 + 4

    // Pill bg (dark + border)
    const lx = labelCenterX - labelW / 2
    const ly = labelY - labelH / 2
    roundedRect(ctx, lx, ly, labelW, labelH, 4)
    ctx.fillStyle = 'rgba(15, 23, 42, 0.92)'
    ctx.fill()
    ctx.strokeStyle = color.stroke
    ctx.lineWidth = 1
    roundedRect(ctx, lx, ly, labelW, labelH, 4)
    ctx.stroke()

    // Label text
    ctx.fillStyle = color.text
    ctx.textAlign = 'center'
    ctx.fillText(labelText, labelCenterX, labelY)

    // Connector line from label to box edge
    ctx.strokeStyle = color.stroke
    ctx.lineWidth = 0.8
    ctx.beginPath()
    if (placeAbove) {
      ctx.moveTo(labelCenterX, labelY + labelH / 2)
      ctx.lineTo(labelCenterX, y0)
    } else {
      ctx.moveTo(labelCenterX, labelY - labelH / 2)
      ctx.lineTo(labelCenterX, y1)
    }
    ctx.stroke()
  }, [chart, series, candles, pattern, width, height])

  // Re-render saat pan/zoom
  useEffect(() => {
    if (!chart) return
    const ts = chart.timeScale()
    const handler = () => render()
    ts.subscribeVisibleLogicalRangeChange(handler)
    return () => ts.unsubscribeVisibleLogicalRangeChange(handler)
  }, [chart, render])

  useEffect(() => { render() }, [render])

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
        zIndex: 2,  // di bawah PositionBoxOverlay (z=3) supaya entry/SL/TP tetap di atas
      }}
    />
  )
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
