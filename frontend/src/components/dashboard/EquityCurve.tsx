'use client'

import { useEffect, useMemo, useRef } from 'react'
import {
  createChart,
  ColorType,
  AreaSeries,
  type IChartApi,
  type ISeriesApi,
  type UTCTimestamp,
} from 'lightweight-charts'
import type { TradePositionResponse } from '@/lib/types'

interface Props {
  positions: TradePositionResponse[]
  baseline: number     // starting equity (mis. $1000)
  currentEquity: number
  height?: number
}

/**
 * Mini sparkline equity curve: derived dari closed positions (cumulative P&L
 * sejak baseline) + titik terakhir = live currentEquity.
 */
export function EquityCurve({ positions, baseline, currentEquity, height = 56 }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const chartRef = useRef<IChartApi | null>(null)
  const seriesRef = useRef<ISeriesApi<'Area'> | null>(null)

  const points = useMemo(() => {
    const closed = positions
      .filter((p) => p.status === 'CLOSED_WIN' || p.status === 'CLOSED_LOSS')
      .filter((p) => p.closedAt !== null)
      .sort((a, b) => new Date(a.closedAt!).getTime() - new Date(b.closedAt!).getTime())

    if (closed.length === 0) return [] as { time: UTCTimestamp; value: number }[]

    // Starting point: 1 detik sebelum trade pertama, di nilai baseline
    const firstTime = Math.floor(new Date(closed[0].closedAt!).getTime() / 1000)
    const pts: { time: UTCTimestamp; value: number }[] = [
      { time: (firstTime - 1) as UTCTimestamp, value: baseline },
    ]

    let cum = baseline
    for (const p of closed) {
      cum += p.floatingPnl
      const t = Math.floor(new Date(p.closedAt!).getTime() / 1000) as UTCTimestamp
      // lightweight-charts butuh strictly ascending time
      const lastTime = pts[pts.length - 1].time as number
      const adjusted = (t as number) <= lastTime ? ((lastTime + 1) as UTCTimestamp) : t
      pts.push({ time: adjusted, value: cum })
    }

    // Titik terakhir = live equity (untuk lihat unrealized PnL juga)
    const nowTs = Math.floor(Date.now() / 1000) as UTCTimestamp
    const lastTime = pts[pts.length - 1].time as number
    const adjusted = (nowTs as number) <= lastTime ? ((lastTime + 1) as UTCTimestamp) : nowTs
    if (Math.abs(currentEquity - cum) > 0.01) {
      pts.push({ time: adjusted, value: currentEquity })
    }

    return pts
  }, [positions, baseline, currentEquity])

  const trend = points.length >= 2 ? points[points.length - 1].value - points[0].value : 0
  const isUp = trend >= 0

  useEffect(() => {
    if (!containerRef.current) return

    const chart = createChart(containerRef.current, {
      layout: {
        background: { type: ColorType.Solid, color: 'transparent' },
        textColor: 'transparent',
        fontSize: 8,
      },
      grid: {
        vertLines: { visible: false },
        horzLines: { visible: false },
      },
      rightPriceScale: { visible: false },
      leftPriceScale: { visible: false },
      timeScale: { visible: false, borderVisible: false },
      crosshair: {
        horzLine: { visible: false },
        vertLine: { visible: false },
      },
      handleScale: false,
      handleScroll: false,
      width: containerRef.current.clientWidth,
      height,
    })

    const series = chart.addSeries(AreaSeries, {
      lineColor: '#22c55e',
      topColor: 'rgba(34,197,94,0.3)',
      bottomColor: 'rgba(34,197,94,0.0)',
      lineWidth: 2,
      priceLineVisible: false,
      lastValueVisible: false,
    })

    chartRef.current = chart
    seriesRef.current = series

    const ro = new ResizeObserver(() => {
      if (containerRef.current)
        chart.applyOptions({ width: containerRef.current.clientWidth })
    })
    ro.observe(containerRef.current)

    return () => {
      ro.disconnect()
      chart.remove()
      chartRef.current = null
      seriesRef.current = null
    }
  }, [height])

  useEffect(() => {
    if (!seriesRef.current) return
    const color = isUp ? '#22c55e' : '#ef4444'
    seriesRef.current.applyOptions({
      lineColor: color,
      topColor: isUp ? 'rgba(34,197,94,0.3)' : 'rgba(239,68,68,0.3)',
      bottomColor: isUp ? 'rgba(34,197,94,0.0)' : 'rgba(239,68,68,0.0)',
    })
    seriesRef.current.setData(points)
    chartRef.current?.timeScale().fitContent()
  }, [points, isUp])

  if (points.length < 2) {
    return (
      <div className="text-[10px] text-muted-foreground/60 italic h-[56px] flex items-center justify-center">
        Belum ada trade closed
      </div>
    )
  }

  return (
    <div className="relative">
      <div ref={containerRef} className="w-full" style={{ height }} />
      <div className="absolute top-0 right-0 text-[10px] font-mono">
        <span className={isUp ? 'text-emerald-500' : 'text-red-500'}>
          {isUp ? '+' : ''}
          {trend.toFixed(2)}
        </span>
      </div>
    </div>
  )
}
