'use client'

import { useCallback, useEffect, useRef } from 'react'
import type { IChartApi, ISeriesApi } from 'lightweight-charts'
import type { FvgZoneDto } from '@/lib/types'

interface Props {
  chart: IChartApi | null
  series: ISeriesApi<'Candlestick'> | null
  zones: FvgZoneDto[]
  width: number
  height: number
}

/**
 * Render Fair Value Gap zones di chart. FVG = 3-candle pattern dengan gap
 * antara candle[i-2] high dan candle[i].low (bullish) atau sebaliknya (bearish).
 *
 * <para>Visualization style:</para>
 * <list type="bullet">
 *   <item>Unfilled FVG (price belum revisit): solid colored band, anchored mulai dari formedAt → extend ke kanan</item>
 *   <item>Filled FVG (price sudah revisit & close): faded outline saja</item>
 *   <item>Bullish FVG (price gap down filled, bias up): semi-transparent emerald</item>
 *   <item>Bearish FVG (price gap up filled, bias down): semi-transparent red</item>
 * </list>
 *
 * Pointer-events: none — read-only overlay.
 */
export function FvgOverlay({ chart, series, zones, width, height }: Props) {
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

    if (!chart || !series || zones.length === 0) return
    const ts = chart.timeScale()

    for (const z of zones) {
      // Convert (time, price) → canvas (x, y)
      const xStart = ts.timeToCoordinate(z.formedAt as unknown as Parameters<typeof ts.timeToCoordinate>[0])
      if (xStart === null) continue
      const yTop = series.priceToCoordinate(z.top)
      const yBot = series.priceToCoordinate(z.bottom)
      if (yTop === null || yBot === null) continue

      const x = xStart as number
      const top = Math.min(yTop as number, yBot as number)
      const h = Math.abs((yBot as number) - (yTop as number))
      const w = width - x  // extend ke kanan

      const isBull = z.bias === 'Bullish'
      const rgb = isBull ? '34, 197, 94' : '239, 68, 68'

      if (z.filled) {
        // Filled FVG — sangat samar, hanya outline
        ctx.strokeStyle = `rgba(${rgb}, 0.20)`
        ctx.lineWidth = 0.5
        ctx.setLineDash([2, 4])
        ctx.strokeRect(x, top, w, h)
        ctx.setLineDash([])
      } else {
        // Unfilled FVG — actionable zone
        ctx.fillStyle = `rgba(${rgb}, 0.10)`
        ctx.fillRect(x, top, w, h)
        // Edge lines (top + bottom of gap)
        ctx.strokeStyle = `rgba(${rgb}, 0.5)`
        ctx.lineWidth = 1
        ctx.setLineDash([4, 3])
        ctx.beginPath()
        ctx.moveTo(x, top); ctx.lineTo(width, top)
        ctx.moveTo(x, top + h); ctx.lineTo(width, top + h)
        ctx.stroke()
        ctx.setLineDash([])

        // Label di kanan: "FVG 8p ↓" / "FVG 12p ↑"
        const labelText = `FVG ${z.sizePips.toFixed(0)}p ${isBull ? '↑' : '↓'}`
        ctx.font = '10px monospace'
        const metrics = ctx.measureText(labelText)
        const padX = 4
        const boxW = metrics.width + padX * 2
        const boxH = 14
        const boxX = Math.max(2, width - boxW - 4)
        const boxY = top + h / 2 - boxH / 2

        ctx.fillStyle = `rgba(${rgb}, 0.85)`
        ctx.fillRect(boxX, boxY, boxW, boxH)
        ctx.fillStyle = '#ffffff'
        ctx.textBaseline = 'middle'
        ctx.fillText(labelText, boxX + padX, boxY + boxH / 2)
      }
    }
  }, [chart, series, zones, width, height])

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
        zIndex: 1,  // di bawah pattern/position overlays
      }}
    />
  )
}
