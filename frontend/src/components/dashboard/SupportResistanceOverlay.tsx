'use client'

import { useCallback, useEffect, useRef } from 'react'
import type { IChartApi, ISeriesApi } from 'lightweight-charts'

interface Props {
  chart: IChartApi | null
  series: ISeriesApi<'Candlestick'> | null
  support: number | null
  resistance: number | null
  width: number
  height: number
}

/**
 * Render Support/Resistance sebagai semi-transparent ZONE (band ±5p) di chart.
 * Lebih obvious dari single priceLine — trader bisa lihat "zona reject" dengan jelas.
 * Pointer-events:none — read-only overlay.
 */
export function SupportResistanceOverlay({ chart, series, support, resistance, width, height }: Props) {
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

    const zonePips = 5
    const zoneHeight = 0.0001 * zonePips  // 5 pip band (EURUSD)

    // Support zone (green)
    if (support && support > 0) {
      const yMid = series.priceToCoordinate(support) as number | null
      const yTop = series.priceToCoordinate(support + zoneHeight) as number | null
      const yBot = series.priceToCoordinate(support - zoneHeight) as number | null
      if (yMid !== null && yTop !== null && yBot !== null) {
        ctx.fillStyle = 'rgba(34, 197, 94, 0.12)'
        ctx.fillRect(0, yTop, width, yBot - yTop)
        // Edge lines
        ctx.strokeStyle = 'rgba(34, 197, 94, 0.4)'
        ctx.lineWidth = 0.5
        ctx.setLineDash([3, 3])
        ctx.beginPath()
        ctx.moveTo(0, yTop); ctx.lineTo(width, yTop)
        ctx.moveTo(0, yBot); ctx.lineTo(width, yBot)
        ctx.stroke()
        ctx.setLineDash([])
      }
    }

    // Resistance zone (red)
    if (resistance && resistance > 0) {
      const yMid = series.priceToCoordinate(resistance) as number | null
      const yTop = series.priceToCoordinate(resistance + zoneHeight) as number | null
      const yBot = series.priceToCoordinate(resistance - zoneHeight) as number | null
      if (yMid !== null && yTop !== null && yBot !== null) {
        ctx.fillStyle = 'rgba(239, 68, 68, 0.12)'
        ctx.fillRect(0, yTop, width, yBot - yTop)
        ctx.strokeStyle = 'rgba(239, 68, 68, 0.4)'
        ctx.lineWidth = 0.5
        ctx.setLineDash([3, 3])
        ctx.beginPath()
        ctx.moveTo(0, yTop); ctx.lineTo(width, yTop)
        ctx.moveTo(0, yBot); ctx.lineTo(width, yBot)
        ctx.stroke()
        ctx.setLineDash([])
      }
    }
  }, [chart, series, support, resistance, width, height])

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
        zIndex: 1,  // di paling bawah, di belakang pattern/position overlays
      }}
    />
  )
}
