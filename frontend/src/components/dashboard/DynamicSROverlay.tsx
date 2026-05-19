'use client'

import { useCallback, useEffect, useRef } from 'react'
import type { IChartApi, ISeriesApi } from 'lightweight-charts'

export interface SwingPoint {
  type: 'High' | 'Low'
  price: number
  time: number  // Unix seconds
}

export interface Trendline {
  startTime: number
  startPrice: number
  endTime: number
  endPrice: number
  direction: 'Ascending' | 'Descending' | 'Flat'
  strength: 'Strong' | 'Good' | 'Weak'
  slopePipsPerHour: number
}

interface Props {
  chart: IChartApi | null
  series: ISeriesApi<'Candlestick'> | null
  swingHighs: SwingPoint[]
  swingLows: SwingPoint[]
  dynamicResistance: Trendline | null
  dynamicSupport: Trendline | null
  width: number
  height: number
}

/**
 * Render Dynamic Resistance + Dynamic Support sebagai trendline yang connect
 * swing pivots terakhir, plus small markers di tiap pivot point.
 *
 * <para>Versi dynamic dari static S/R: line miring (uptrend/downtrend channel)
 * vs horizontal level. Lebih reflective dari structure aktual market.</para>
 *
 * Pointer-events: none — read-only overlay.
 */
export function DynamicSROverlay({
  chart, series, swingHighs, swingLows,
  dynamicResistance, dynamicSupport, width, height,
}: Props) {
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

    if (!chart || !series) return
    const ts = chart.timeScale()

    // ── Helper to convert (time, price) → canvas (x, y) ────────────
    const toXY = (time: number, price: number): { x: number; y: number } | null => {
      const x = ts.timeToCoordinate(time as unknown as Parameters<typeof ts.timeToCoordinate>[0])
      const y = series.priceToCoordinate(price)
      if (x === null || y === null) return null
      return { x: x as number, y: y as number }
    }

    // ── Draw trendline (resistance / support) ──────────────────────
    const drawTrendline = (line: Trendline | null, colorRgb: string, label: string) => {
      if (!line) return
      const a = toXY(line.startTime, line.startPrice)
      const b = toXY(line.endTime, line.endPrice)
      if (!a || !b) return

      // Map strength → opacity + line width
      const opacity = line.strength === 'Strong' ? 0.9 : line.strength === 'Good' ? 0.65 : 0.4
      const lineWidth = line.strength === 'Strong' ? 2 : 1.5

      ctx.strokeStyle = `rgba(${colorRgb}, ${opacity})`
      ctx.lineWidth = lineWidth
      ctx.setLineDash([6, 4])
      ctx.beginPath()
      ctx.moveTo(a.x, a.y)
      ctx.lineTo(b.x, b.y)
      ctx.stroke()
      ctx.setLineDash([])

      // Label badge di ujung kanan
      const labelText = `${label} (${line.strength})`
      ctx.font = '10px monospace'
      const metrics = ctx.measureText(labelText)
      const padX = 4, padY = 2
      const boxW = metrics.width + padX * 2
      const boxH = 14
      const boxX = Math.min(b.x, width - boxW - 2)
      const boxY = b.y - boxH / 2

      ctx.fillStyle = `rgba(${colorRgb}, ${opacity})`
      ctx.fillRect(boxX, boxY, boxW, boxH)
      ctx.fillStyle = '#ffffff'
      ctx.textBaseline = 'middle'
      ctx.fillText(labelText, boxX + padX, boxY + boxH / 2)
    }

    // ── Draw swing pivot markers ───────────────────────────────────
    const drawPivots = (pivots: SwingPoint[], colorRgb: string) => {
      pivots.forEach((p) => {
        const pt = toXY(p.time, p.price)
        if (!pt) return
        ctx.fillStyle = `rgba(${colorRgb}, 0.85)`
        ctx.beginPath()
        ctx.arc(pt.x, pt.y, 3, 0, Math.PI * 2)
        ctx.fill()
        ctx.strokeStyle = `rgba(${colorRgb}, 1)`
        ctx.lineWidth = 1
        ctx.stroke()
      })
    }

    // Resistance (red): line + high pivots
    drawTrendline(dynamicResistance, '239, 68, 68', 'Dyn R')
    drawPivots(swingHighs, '239, 68, 68')

    // Support (green): line + low pivots
    drawTrendline(dynamicSupport, '34, 197, 94', 'Dyn S')
    drawPivots(swingLows, '34, 197, 94')
  }, [chart, series, swingHighs, swingLows, dynamicResistance, dynamicSupport, width, height])

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
        zIndex: 2,  // di atas SupportResistanceOverlay (static), di bawah PatternHighlight
      }}
    />
  )
}
