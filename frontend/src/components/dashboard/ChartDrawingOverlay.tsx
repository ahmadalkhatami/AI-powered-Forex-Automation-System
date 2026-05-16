'use client'

import { useCallback, useEffect, useRef, useState } from 'react'
import type { IChartApi, ISeriesApi, Time, UTCTimestamp, MouseEventParams } from 'lightweight-charts'
import {
  type Drawing,
  type DrawingPoint,
  type DrawingType,
  DEFAULT_STYLES,
  hitTest,
  makeId,
} from '@/lib/drawings'
import type { ToolMode } from './ChartToolbar'

interface Props {
  chart: IChartApi | null
  series: ISeriesApi<'Candlestick'> | null
  activeTool: ToolMode
  drawings: Drawing[]
  onDrawingsChange: (drawings: Drawing[]) => void
  selectedId: string | null
  onSelectedIdChange: (id: string | null) => void
  width: number
  height: number
}

/**
 * Canvas overlay di atas chart untuk render & interaksi drawing.
 *
 * Pointer events strategy:
 * - activeTool === drawing tool: pointer-events:auto, capture clicks untuk create
 * - activeTool === cursor:
 *    - hover-over-drawing: pointer-events:auto (klik untuk select)
 *    - hover-empty:        pointer-events:none (chart receives pan/zoom)
 *   Tracking pakai chart.subscribeCrosshairMove karena fires meski overlay
 *   pointer-events:none (event sampai chart canvas di bawahnya).
 */
export function ChartDrawingOverlay({
  chart,
  series,
  activeTool,
  drawings,
  onDrawingsChange,
  selectedId,
  onSelectedIdChange,
  width,
  height,
}: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  // Drawing-in-progress: first point sudah ditaruh, menunggu point berikutnya
  const [pendingPoint, setPendingPoint] = useState<DrawingPoint | null>(null)
  // Pixel-tracking mouse untuk preview saat drawing in-progress
  const [hoverPixel, setHoverPixel] = useState<{ x: number; y: number } | null>(null)
  // Apakah kursor sedang di atas drawing existing (cursor mode only)
  const [hoveringDrawing, setHoveringDrawing] = useState(false)

  // Konversi data point → pixel coord
  const dataToPixel = useCallback(
    (point: DrawingPoint): { x: number; y: number } | null => {
      if (!chart || !series) return null
      const x = chart.timeScale().timeToCoordinate(point.time as UTCTimestamp)
      const y = series.priceToCoordinate(point.price)
      if (x === null || y === null) return null
      return { x, y }
    },
    [chart, series],
  )

  // Konversi pixel → data point
  const pixelToData = useCallback(
    (px: number, py: number): DrawingPoint | null => {
      if (!chart || !series) return null
      const time = chart.timeScale().coordinateToTime(px) as Time | null
      const price = series.coordinateToPrice(py)
      if (time === null || price === null || typeof time !== 'number') return null
      return { time: time as number, price }
    },
    [chart, series],
  )

  // Render semua drawing + preview ke canvas
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

    for (const d of drawings) {
      drawShape(ctx, d, dataToPixel, width, height, d.id === selectedId)
    }

    // Preview drawing-in-progress
    if (pendingPoint && hoverPixel) {
      const start = dataToPixel(pendingPoint)
      if (start) {
        const previewStyle = DEFAULT_STYLES[activeTool as DrawingType] || { color: '#888', width: 1 }
        ctx.strokeStyle = previewStyle.color
        ctx.lineWidth = previewStyle.width
        ctx.setLineDash([4, 4])
        if (activeTool === 'trendline') {
          ctx.beginPath()
          ctx.moveTo(start.x, start.y)
          ctx.lineTo(hoverPixel.x, hoverPixel.y)
          ctx.stroke()
        } else if (activeTool === 'ray') {
          const end = extendRay(start, hoverPixel, width, height)
          ctx.beginPath()
          ctx.moveTo(start.x, start.y)
          ctx.lineTo(end.x, end.y)
          ctx.stroke()
        } else if (activeTool === 'rectangle') {
          ctx.strokeRect(
            Math.min(start.x, hoverPixel.x),
            Math.min(start.y, hoverPixel.y),
            Math.abs(hoverPixel.x - start.x),
            Math.abs(hoverPixel.y - start.y),
          )
        } else if (activeTool === 'measure') {
          drawMeasure(ctx, start, hoverPixel, pendingPoint, pixelToData(hoverPixel.x, hoverPixel.y))
        }
        ctx.setLineDash([])
      }
    }
  }, [drawings, dataToPixel, pixelToData, width, height, selectedId, pendingPoint, hoverPixel, activeTool])

  // Subscribe ke visible range change untuk redraw saat zoom/pan
  useEffect(() => {
    if (!chart) return
    const ts = chart.timeScale()
    const handler = () => render()
    ts.subscribeVisibleLogicalRangeChange(handler)
    return () => {
      ts.unsubscribeVisibleLogicalRangeChange(handler)
    }
  }, [chart, render])

  // Track cursor position via chart crosshair untuk hover detection (cursor mode)
  useEffect(() => {
    if (!chart) return
    const handler = (param: MouseEventParams) => {
      if (activeTool !== 'cursor') {
        setHoveringDrawing(false)
        return
      }
      if (!param.point) {
        setHoveringDrawing(false)
        return
      }
      const { x, y } = param.point
      const isOver = drawings.some((d) => hitTest(d, x, y, dataToPixel))
      setHoveringDrawing(isOver)
    }
    chart.subscribeCrosshairMove(handler)
    return () => chart.unsubscribeCrosshairMove(handler)
  }, [chart, drawings, dataToPixel, activeTool])

  // Redraw saat dependency render() berubah
  useEffect(() => {
    render()
  }, [render])

  // Click handler — hanya fires saat pointer-events:auto
  const handleClick = useCallback(
    (e: React.MouseEvent<HTMLCanvasElement>) => {
      const rect = (e.target as HTMLCanvasElement).getBoundingClientRect()
      const px = e.clientX - rect.left
      const py = e.clientY - rect.top

      // Cursor mode: hit-test untuk select
      if (activeTool === 'cursor') {
        const hit = drawings.find((d) => hitTest(d, px, py, dataToPixel))
        onSelectedIdChange(hit?.id ?? null)
        return
      }

      const dataPoint = pixelToData(px, py)
      if (!dataPoint) return

      // 1-click tools
      if (activeTool === 'hline') {
        addDrawing({
          id: makeId(),
          type: 'hline',
          points: [dataPoint],
          style: { ...DEFAULT_STYLES.hline },
          createdAt: new Date().toISOString(),
        })
        return
      }

      if (activeTool === 'text') {
        const text = window.prompt('Annotation text:', '')?.trim()
        if (!text) return
        addDrawing({
          id: makeId(),
          type: 'text',
          points: [dataPoint],
          style: { ...DEFAULT_STYLES.text },
          createdAt: new Date().toISOString(),
          text,
        })
        return
      }

      // 2-click tools: trendline, rectangle, ray, measure
      if (!pendingPoint) {
        setPendingPoint(dataPoint)
      } else {
        addDrawing({
          id: makeId(),
          type: activeTool as DrawingType,
          points: [pendingPoint, dataPoint],
          style: { ...DEFAULT_STYLES[activeTool as DrawingType] },
          createdAt: new Date().toISOString(),
        })
        setPendingPoint(null)
        setHoverPixel(null)
      }
    },
    [activeTool, drawings, dataToPixel, pixelToData, pendingPoint, onSelectedIdChange],
  )

  const addDrawing = (drawing: Drawing) => {
    onDrawingsChange([...drawings, drawing])
  }

  const handleMouseMove = useCallback(
    (e: React.MouseEvent<HTMLCanvasElement>) => {
      if (!pendingPoint) return
      const rect = (e.target as HTMLCanvasElement).getBoundingClientRect()
      setHoverPixel({ x: e.clientX - rect.left, y: e.clientY - rect.top })
    },
    [pendingPoint],
  )

  // Cancel drawing-in-progress on ESC
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setPendingPoint(null)
        setHoverPixel(null)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  // Reset pending kalau ganti tool
  useEffect(() => {
    setPendingPoint(null)
    setHoverPixel(null)
  }, [activeTool])

  // Cursor mode + hover di drawing → capture click untuk select.
  // Cursor mode + tidak hover → pass-through ke chart untuk pan/zoom.
  // Drawing tool aktif → selalu capture.
  const pointerActive = activeTool !== 'cursor' || hoveringDrawing
  const cursorStyle =
    activeTool === 'cursor'
      ? (hoveringDrawing ? 'pointer' : 'default')
      : 'crosshair'

  return (
    <canvas
      ref={canvasRef}
      onClick={handleClick}
      onMouseMove={handleMouseMove}
      style={{
        position: 'absolute',
        top: 0,
        left: 0,
        width,
        height,
        pointerEvents: pointerActive ? 'auto' : 'none',
        cursor: cursorStyle,
        zIndex: 5,
      }}
    />
  )
}

/**
 * Extend ray dari `from` melewati `through` sampai ujung kanvas.
 * Return titik di edge yang dilewati garis.
 */
function extendRay(
  from: { x: number; y: number },
  through: { x: number; y: number },
  width: number,
  height: number,
): { x: number; y: number } {
  const dx = through.x - from.x
  const dy = through.y - from.y
  if (dx === 0 && dy === 0) return through

  // Cari t terbesar > 0 sehingga (from + t*delta) masih dalam canvas
  const ts: number[] = []
  if (dx > 0) ts.push((width - from.x) / dx)
  if (dx < 0) ts.push((0 - from.x) / dx)
  if (dy > 0) ts.push((height - from.y) / dy)
  if (dy < 0) ts.push((0 - from.y) / dy)
  const validTs = ts.filter((t) => t > 0)
  if (validTs.length === 0) return through
  const t = Math.min(...validTs)
  return { x: from.x + t * dx, y: from.y + t * dy }
}

/**
 * Draw measure box (preview saat drag, atau persisted setelah klik kedua).
 * Shows pips diff + % change + time span.
 */
function drawMeasure(
  ctx: CanvasRenderingContext2D,
  start: { x: number; y: number },
  end: { x: number; y: number },
  startData: DrawingPoint,
  endData: DrawingPoint | null,
) {
  // Box semi-transparan
  ctx.fillStyle = 'rgba(96, 165, 250, 0.12)'
  ctx.fillRect(
    Math.min(start.x, end.x),
    Math.min(start.y, end.y),
    Math.abs(end.x - start.x),
    Math.abs(end.y - start.y),
  )
  ctx.strokeStyle = 'rgba(96, 165, 250, 0.6)'
  ctx.lineWidth = 1
  ctx.strokeRect(
    Math.min(start.x, end.x),
    Math.min(start.y, end.y),
    Math.abs(end.x - start.x),
    Math.abs(end.y - start.y),
  )

  if (!endData) return
  const priceDelta = endData.price - startData.price
  const pips = Math.round(priceDelta * 10000)
  const pct = (priceDelta / startData.price) * 100
  const timeDelta = endData.time - startData.time
  const timeLabel = formatTimeDelta(timeDelta)

  // Label background + text
  const label = `${pips >= 0 ? '+' : ''}${pips} pip · ${pct >= 0 ? '+' : ''}${pct.toFixed(3)}% · ${timeLabel}`
  ctx.font = '11px ui-monospace, SFMono-Regular, monospace'
  const metrics = ctx.measureText(label)
  const padX = 6, padY = 4
  const lblX = Math.min(start.x, end.x) + 4
  const lblY = Math.min(start.y, end.y) - 20
  ctx.fillStyle = 'rgba(15, 23, 42, 0.9)'
  ctx.fillRect(lblX, lblY, metrics.width + padX * 2, 16 + padY)
  ctx.fillStyle = priceDelta >= 0 ? '#22c55e' : '#ef4444'
  ctx.textBaseline = 'top'
  ctx.fillText(label, lblX + padX, lblY + padY)
}

function formatTimeDelta(seconds: number): string {
  const abs = Math.abs(seconds)
  if (abs < 3600) return `${Math.round(abs / 60)}m`
  if (abs < 86400) return `${(abs / 3600).toFixed(1)}h`
  return `${(abs / 86400).toFixed(1)}d`
}

function drawShape(
  ctx: CanvasRenderingContext2D,
  d: Drawing,
  dataToPixel: (p: DrawingPoint) => { x: number; y: number } | null,
  canvasWidth: number,
  canvasHeight: number,
  selected: boolean,
) {
  ctx.strokeStyle = d.style.color
  ctx.lineWidth = selected ? d.style.width + 1 : d.style.width
  ctx.setLineDash(d.style.dashed ? [6, 4] : [])

  if (d.type === 'hline') {
    const a = dataToPixel(d.points[0])
    if (!a) return
    ctx.beginPath()
    ctx.moveTo(0, a.y)
    ctx.lineTo(canvasWidth, a.y)
    ctx.stroke()
    ctx.setLineDash([])
    ctx.fillStyle = d.style.color
    ctx.font = '10px ui-monospace, SFMono-Regular, monospace'
    ctx.textAlign = 'right'
    ctx.textBaseline = 'middle'
    ctx.fillText(d.points[0].price.toFixed(5), canvasWidth - 6, a.y - 6)
  } else if (d.type === 'trendline') {
    const a = dataToPixel(d.points[0])
    const b = dataToPixel(d.points[1])
    if (!a || !b) return
    ctx.beginPath()
    ctx.moveTo(a.x, a.y)
    ctx.lineTo(b.x, b.y)
    ctx.stroke()
  } else if (d.type === 'ray') {
    const a = dataToPixel(d.points[0])
    const b = dataToPixel(d.points[1])
    if (!a || !b) return
    const end = extendRay(a, b, canvasWidth, canvasHeight)
    ctx.beginPath()
    ctx.moveTo(a.x, a.y)
    ctx.lineTo(end.x, end.y)
    ctx.stroke()
  } else if (d.type === 'rectangle') {
    const a = dataToPixel(d.points[0])
    const b = dataToPixel(d.points[1])
    if (!a || !b) return
    const x = Math.min(a.x, b.x)
    const y = Math.min(a.y, b.y)
    const w = Math.abs(b.x - a.x)
    const h = Math.abs(b.y - a.y)
    ctx.fillStyle = d.style.color.includes('rgba')
      ? d.style.color
      : d.style.color + '22'
    ctx.fillRect(x, y, w, h)
    ctx.setLineDash([])
    ctx.strokeRect(x, y, w, h)
  } else if (d.type === 'text') {
    const a = dataToPixel(d.points[0])
    if (!a) return
    ctx.setLineDash([])
    ctx.font = '12px system-ui, -apple-system, sans-serif'
    ctx.textAlign = 'left'
    ctx.textBaseline = 'middle'
    const text = d.text ?? ''
    const metrics = ctx.measureText(text)
    const padX = 4, padY = 2
    // Background pill
    ctx.fillStyle = 'rgba(15, 23, 42, 0.85)'
    ctx.fillRect(a.x - padX, a.y - 8, metrics.width + padX * 2, 16 + padY)
    ctx.fillStyle = d.style.color
    ctx.fillText(text, a.x, a.y)
  } else if (d.type === 'measure') {
    const a = dataToPixel(d.points[0])
    const b = dataToPixel(d.points[1])
    if (!a || !b) return
    drawMeasure(ctx, a, b, d.points[0], d.points[1])
  }

  // Selection highlight: titik kecil di endpoints
  if (selected) {
    ctx.setLineDash([])
    ctx.fillStyle = '#ffffff'
    ctx.strokeStyle = d.style.color
    ctx.lineWidth = 2
    for (const p of d.points) {
      const c = dataToPixel(p)
      if (!c) continue
      ctx.beginPath()
      ctx.arc(c.x, c.y, 4, 0, Math.PI * 2)
      ctx.fill()
      ctx.stroke()
    }
  }
}
