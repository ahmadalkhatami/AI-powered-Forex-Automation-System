'use client'

import { useCallback, useEffect, useRef } from 'react'
import type { IChartApi, ISeriesApi } from 'lightweight-charts'
import type { OrderBlockDto } from '@/lib/types'

interface Props {
  chart: IChartApi | null
  series: ISeriesApi<'Candlestick'> | null
  blocks: OrderBlockDto[]
  width: number
  height: number
}

/**
 * Render Order Block (OB) zones di chart. OB = last opposite-direction candle
 * sebelum strong impulse move — SMC zone yang sering di-mitigate sebelum continuation.
 *
 * <para>Visual distinguishing dari FVG:</para>
 * <list type="bullet">
 *   <item>OB pakai SOLID border (vs FVG dashed)</item>
 *   <item>OB pakai diagonal hatching pattern (vs FVG solid fill) untuk discriminate visual</item>
 *   <item>Unmitigated OB lebih bold, Mitigated lebih faded</item>
 * </list>
 *
 * Pointer-events: none — read-only overlay.
 */
export function OrderBlockOverlay({ chart, series, blocks, width, height }: Props) {
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

    if (!chart || !series || blocks.length === 0) return
    const ts = chart.timeScale()

    for (const ob of blocks) {
      const xStart = ts.timeToCoordinate(ob.formedAt as unknown as Parameters<typeof ts.timeToCoordinate>[0])
      if (xStart === null) continue
      const yTop = series.priceToCoordinate(ob.top)
      const yBot = series.priceToCoordinate(ob.bottom)
      if (yTop === null || yBot === null) continue

      const x = xStart as number
      const top = Math.min(yTop as number, yBot as number)
      const h = Math.abs((yBot as number) - (yTop as number))
      const w = width - x

      const isBull = ob.bias === 'Bullish'
      const rgb = isBull ? '34, 197, 94' : '239, 68, 68'
      const baseAlpha = ob.mitigated ? 0.25 : 0.7

      // Diagonal hatching fill (distinct dari FVG solid)
      ctx.save()
      ctx.beginPath()
      ctx.rect(x, top, w, h)
      ctx.clip()

      const fillAlpha = ob.mitigated ? 0.04 : 0.12
      ctx.fillStyle = `rgba(${rgb}, ${fillAlpha})`
      ctx.fillRect(x, top, w, h)

      // Diagonal lines untuk hatching effect
      const spacing = 8
      ctx.strokeStyle = `rgba(${rgb}, ${ob.mitigated ? 0.10 : 0.25})`
      ctx.lineWidth = 0.5
      ctx.beginPath()
      for (let dx = -h; dx < w + h; dx += spacing) {
        ctx.moveTo(x + dx, top)
        ctx.lineTo(x + dx + h, top + h)
      }
      ctx.stroke()
      ctx.restore()

      // SOLID border (distinct dari FVG dashed)
      ctx.strokeStyle = `rgba(${rgb}, ${baseAlpha})`
      ctx.lineWidth = ob.mitigated ? 0.5 : 1.5
      ctx.strokeRect(x, top, w, h)

      // Label badge "OB ↑ 12p" — di sebelah kiri zone (anchored ke formedAt)
      if (!ob.mitigated) {
        const labelText = `OB ${isBull ? '↑' : '↓'} ${ob.sizePips.toFixed(0)}p`
        ctx.font = 'bold 10px monospace'
        const metrics = ctx.measureText(labelText)
        const padX = 4
        const boxW = metrics.width + padX * 2
        const boxH = 14
        const boxX = Math.max(2, x + 2)
        const boxY = Math.max(2, top - boxH - 2)

        ctx.fillStyle = `rgba(${rgb}, 0.95)`
        ctx.fillRect(boxX, boxY, boxW, boxH)
        ctx.fillStyle = '#ffffff'
        ctx.textBaseline = 'middle'
        ctx.fillText(labelText, boxX + padX, boxY + boxH / 2)
      }
    }
  }, [chart, series, blocks, width, height])

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
        zIndex: 1,  // di bawah pattern/position overlay
      }}
    />
  )
}
