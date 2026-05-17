'use client'

import { useCallback, useEffect, useRef, useState } from 'react'
import type { IChartApi, ISeriesApi, Time, UTCTimestamp, MouseEventParams } from 'lightweight-charts'
import {
  type Drawing,
  type DrawingPoint,
  type DrawingType,
  DEFAULT_STYLES,
  FIB_RETRACEMENT_LEVELS,
  FIB_EXTENSION_LEVELS,
  hitTest,
  makeId,
  snapToCandle as snapPoint,
} from '@/lib/drawings'
import type { CandleBar } from '@/lib/types'
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
  candles: CandleBar[]
  snapEnabled: boolean
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
  candles,
  snapEnabled,
}: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  // Drawing-in-progress: untuk 2-click tool: [start]. Untuk 3-click (fib-extension): [a, b]
  const [pendingPoints, setPendingPoints] = useState<DrawingPoint[]>([])
  // Pixel-tracking mouse untuk preview saat drawing in-progress
  const [hoverPixel, setHoverPixel] = useState<{ x: number; y: number } | null>(null)
  // Apakah kursor sedang di atas drawing existing (cursor mode only)
  const [hoveringDrawing, setHoveringDrawing] = useState(false)
  // Drag-edit endpoint: drawing yang sedang di-drag + index point-nya
  const [dragState, setDragState] = useState<{ drawingId: string; pointIndex: number } | null>(null)
  // Apakah kursor di atas endpoint drawing terpilih (cursor → grab)
  const [hoveringEndpoint, setHoveringEndpoint] = useState(false)

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
    if (pendingPoints.length > 0 && hoverPixel) {
      const first = dataToPixel(pendingPoints[0])
      if (first) {
        const previewStyle = DEFAULT_STYLES[activeTool as DrawingType] || { color: '#888', width: 1 }
        ctx.strokeStyle = previewStyle.color
        ctx.lineWidth = previewStyle.width
        ctx.setLineDash([4, 4])
        if (activeTool === 'trendline') {
          ctx.beginPath()
          ctx.moveTo(first.x, first.y)
          ctx.lineTo(hoverPixel.x, hoverPixel.y)
          ctx.stroke()
        } else if (activeTool === 'ray') {
          const end = extendRay(first, hoverPixel, width, height)
          ctx.beginPath()
          ctx.moveTo(first.x, first.y)
          ctx.lineTo(end.x, end.y)
          ctx.stroke()
        } else if (activeTool === 'rectangle') {
          ctx.strokeRect(
            Math.min(first.x, hoverPixel.x),
            Math.min(first.y, hoverPixel.y),
            Math.abs(hoverPixel.x - first.x),
            Math.abs(hoverPixel.y - first.y),
          )
        } else if (activeTool === 'measure') {
          drawMeasure(ctx, first, hoverPixel, pendingPoints[0], pixelToData(hoverPixel.x, hoverPixel.y))
        } else if (activeTool === 'fib-retracement') {
          const hoverData = pixelToData(hoverPixel.x, hoverPixel.y)
          if (hoverData) {
            drawFibRetracement(
              ctx,
              { ...pendingPoints[0] },
              hoverData,
              dataToPixel,
              width,
              DEFAULT_STYLES['fib-retracement'].color,
            )
          }
        } else if (activeTool === 'fib-extension' && pendingPoints.length >= 1) {
          // Pendrop ke titik A (1 point) atau A+B (2 points)
          ctx.beginPath()
          ctx.moveTo(first.x, first.y)
          ctx.lineTo(hoverPixel.x, hoverPixel.y)
          ctx.stroke()
          if (pendingPoints.length === 2) {
            const second = dataToPixel(pendingPoints[1])
            if (second) {
              ctx.beginPath()
              ctx.moveTo(second.x, second.y)
              ctx.lineTo(hoverPixel.x, hoverPixel.y)
              ctx.stroke()
              // Preview level projection
              const hoverData = pixelToData(hoverPixel.x, hoverPixel.y)
              if (hoverData) {
                drawFibExtension(
                  ctx,
                  pendingPoints[0],
                  pendingPoints[1],
                  hoverData,
                  dataToPixel,
                  width,
                  DEFAULT_STYLES['fib-extension'].color,
                )
              }
            }
          }
        }
        ctx.setLineDash([])
      }
    }
  }, [drawings, dataToPixel, pixelToData, width, height, selectedId, pendingPoints, hoverPixel, activeTool])

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

  // Track cursor position via chart crosshair untuk hover detection (cursor mode).
  // Juga deteksi apakah hovering di endpoint drawing terpilih → enable drag-edit.
  useEffect(() => {
    if (!chart) return
    const handler = (param: MouseEventParams) => {
      if (activeTool !== 'cursor') {
        setHoveringDrawing(false)
        setHoveringEndpoint(false)
        return
      }
      if (!param.point) {
        setHoveringDrawing(false)
        setHoveringEndpoint(false)
        return
      }
      const { x, y } = param.point
      // Filter locked drawings dari hit-test — biar bisa pan-through
      const isOver = drawings.some(
        (d) => !d.locked && hitTest(d, x, y, dataToPixel),
      )
      setHoveringDrawing(isOver)

      // Endpoint hit-test untuk drag-edit (cuma kalau ada selected & unlocked)
      const selected = drawings.find((d) => d.id === selectedId && !d.locked)
      if (selected) {
        const overEndpoint = selected.points.some((p) => {
          const c = dataToPixel(p)
          if (!c) return false
          return Math.hypot(x - c.x, y - c.y) <= 8
        })
        setHoveringEndpoint(overEndpoint)
      } else {
        setHoveringEndpoint(false)
      }
    }
    chart.subscribeCrosshairMove(handler)
    return () => chart.unsubscribeCrosshairMove(handler)
  }, [chart, drawings, dataToPixel, activeTool, selectedId])

  // Redraw saat dependency render() berubah
  useEffect(() => {
    render()
  }, [render])

  // MouseDown: kalau di endpoint drawing terpilih → mulai drag-edit.
  const handleMouseDown = useCallback(
    (e: React.MouseEvent<HTMLCanvasElement>) => {
      if (activeTool !== 'cursor' || !selectedId) return
      const selected = drawings.find((d) => d.id === selectedId)
      if (!selected || selected.locked) return
      const rect = (e.target as HTMLCanvasElement).getBoundingClientRect()
      const px = e.clientX - rect.left
      const py = e.clientY - rect.top
      for (let i = 0; i < selected.points.length; i++) {
        const c = dataToPixel(selected.points[i])
        if (!c) continue
        if (Math.hypot(px - c.x, py - c.y) <= 8) {
          setDragState({ drawingId: selectedId, pointIndex: i })
          e.preventDefault()
          return
        }
      }
    },
    [activeTool, selectedId, drawings, dataToPixel],
  )

  // Window-level mouse listeners untuk drag — pakai window supaya drag tetap
  // tertangkap meski kursor keluar canvas.
  useEffect(() => {
    if (!dragState) return
    const canvas = canvasRef.current
    if (!canvas) return

    const onMove = (e: MouseEvent) => {
      const rect = canvas.getBoundingClientRect()
      const px = e.clientX - rect.left
      const py = e.clientY - rect.top
      let newPoint = pixelToData(px, py)
      if (!newPoint) return
      if (snapEnabled && candles.length > 0) {
        newPoint = snapPoint(newPoint, candles)
      }
      onDrawingsChange(
        drawings.map((d) =>
          d.id === dragState.drawingId
            ? { ...d, points: d.points.map((p, i) => (i === dragState.pointIndex ? newPoint! : p)) }
            : d,
        ),
      )
    }
    const onUp = () => setDragState(null)

    window.addEventListener('mousemove', onMove)
    window.addEventListener('mouseup', onUp)
    return () => {
      window.removeEventListener('mousemove', onMove)
      window.removeEventListener('mouseup', onUp)
    }
  }, [dragState, drawings, onDrawingsChange, pixelToData, snapEnabled, candles])

  // Click handler — hanya fires saat pointer-events:auto
  const handleClick = useCallback(
    (e: React.MouseEvent<HTMLCanvasElement>) => {
      // Kalau baru selesai drag, jangan re-process sebagai click
      if (dragState) return

      const rect = (e.target as HTMLCanvasElement).getBoundingClientRect()
      const px = e.clientX - rect.left
      const py = e.clientY - rect.top

      // Cursor mode: hit-test untuk select (skip locked).
      // dragState tidak di-list di deps karena hanya dipakai untuk early-return
      // di awal handler — selalu pakai nilai latest via closure-capture refresh.
      if (activeTool === 'cursor') {
        const hit = drawings.find((d) => !d.locked && hitTest(d, px, py, dataToPixel))
        onSelectedIdChange(hit?.id ?? null)
        return
      }

      let dataPoint = pixelToData(px, py)
      if (!dataPoint) return
      // Magnet mode: snap ke high/low candle terdekat
      if (snapEnabled && candles.length > 0) {
        dataPoint = snapPoint(dataPoint, candles)
      }

      // 1-click tools
      if (activeTool === 'hline') {
        onDrawingsChange([...drawings, {
          id: makeId(),
          type: 'hline',
          points: [dataPoint],
          style: { ...DEFAULT_STYLES.hline },
          createdAt: new Date().toISOString(),
        }])
        return
      }

      if (activeTool === 'text') {
        const text = window.prompt('Annotation text:', '')?.trim()
        if (!text) return
        onDrawingsChange([...drawings, {
          id: makeId(),
          type: 'text',
          points: [dataPoint],
          style: { ...DEFAULT_STYLES.text },
          createdAt: new Date().toISOString(),
          text,
        }])
        return
      }

      // 3-click tool: fib-extension (A, B, C)
      if (activeTool === 'fib-extension') {
        if (pendingPoints.length < 2) {
          setPendingPoints([...pendingPoints, dataPoint])
        } else {
          onDrawingsChange([...drawings, {
            id: makeId(),
            type: 'fib-extension',
            points: [...pendingPoints, dataPoint],
            style: { ...DEFAULT_STYLES['fib-extension'] },
            createdAt: new Date().toISOString(),
          }])
          setPendingPoints([])
          setHoverPixel(null)
        }
        return
      }

      // 2-click tools: trendline, rectangle, ray, measure, fib-retracement
      if (pendingPoints.length === 0) {
        setPendingPoints([dataPoint])
      } else {
        onDrawingsChange([...drawings, {
          id: makeId(),
          type: activeTool as DrawingType,
          points: [pendingPoints[0], dataPoint],
          style: { ...DEFAULT_STYLES[activeTool as DrawingType] },
          createdAt: new Date().toISOString(),
        }])
        setPendingPoints([])
        setHoverPixel(null)
      }
    },
    [activeTool, drawings, dataToPixel, pixelToData, pendingPoints, onSelectedIdChange, onDrawingsChange, snapEnabled, candles, dragState],
  )

  const handleMouseMove = useCallback(
    (e: React.MouseEvent<HTMLCanvasElement>) => {
      if (pendingPoints.length === 0) return
      const rect = (e.target as HTMLCanvasElement).getBoundingClientRect()
      setHoverPixel({ x: e.clientX - rect.left, y: e.clientY - rect.top })
    },
    [pendingPoints],
  )

  // Cancel drawing-in-progress on ESC
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setPendingPoints([])
        setHoverPixel(null)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  // Reset pending kalau ganti tool
  useEffect(() => {
    setPendingPoints([])
    setHoverPixel(null)
  }, [activeTool])

  // Cursor mode + hover di drawing/endpoint → capture click untuk select/drag.
  // Cursor mode + tidak hover → pass-through ke chart untuk pan/zoom.
  // Drawing tool aktif → selalu capture.
  // Dragging in-progress → capture (window listeners juga aktif untuk antisipasi
  // mouse keluar canvas).
  const pointerActive = activeTool !== 'cursor' || hoveringDrawing || hoveringEndpoint || !!dragState
  const cursorStyle = (() => {
    if (activeTool !== 'cursor') return 'crosshair'
    if (dragState) return 'grabbing'
    if (hoveringEndpoint) return 'grab'
    if (hoveringDrawing) return 'pointer'
    return 'default'
  })()

  return (
    <canvas
      ref={canvasRef}
      onClick={handleClick}
      onMouseDown={handleMouseDown}
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
  } else if (d.type === 'fib-retracement') {
    drawFibRetracement(ctx, d.points[0], d.points[1], dataToPixel, canvasWidth, d.style.color)
  } else if (d.type === 'fib-extension') {
    if (d.points.length < 3) return
    drawFibExtension(ctx, d.points[0], d.points[1], d.points[2], dataToPixel, canvasWidth, d.style.color)
  }

  // Selection highlight: titik kecil di endpoints (kecuali locked)
  if (selected && !d.locked) {
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

  // Lock indicator: 🔒 kecil di endpoint pertama
  if (d.locked) {
    const c = dataToPixel(d.points[0])
    if (c) {
      ctx.setLineDash([])
      ctx.fillStyle = 'rgba(15, 23, 42, 0.85)'
      ctx.fillRect(c.x - 7, c.y - 7, 14, 14)
      ctx.fillStyle = '#9ca3af'
      ctx.font = '10px ui-sans-serif, system-ui'
      ctx.textAlign = 'center'
      ctx.textBaseline = 'middle'
      ctx.fillText('🔒', c.x, c.y + 1)
    }
  }
}

/**
 * Render Fibonacci retracement: 7 horizontal lines antara anchor A dan B,
 * tiap level diberi label price + percentage.
 */
function drawFibRetracement(
  ctx: CanvasRenderingContext2D,
  a: DrawingPoint,
  b: DrawingPoint,
  dataToPixel: (p: DrawingPoint) => { x: number; y: number } | null,
  canvasWidth: number,
  baseColor: string,
) {
  const pa = dataToPixel(a)
  const pb = dataToPixel(b)
  if (!pa || !pb) return
  const x1 = Math.min(pa.x, pb.x)

  ctx.setLineDash([])
  ctx.font = '10px ui-monospace, SFMono-Regular, monospace'
  ctx.textBaseline = 'middle'

  for (const lv of FIB_RETRACEMENT_LEVELS) {
    const y = pa.y + (pb.y - pa.y) * lv
    const price = a.price + (b.price - a.price) * lv
    // Line stroke
    ctx.strokeStyle = baseColor + (lv === 0.5 ? 'ee' : lv === 0.618 || lv === 0.382 ? 'cc' : '88')
    ctx.lineWidth = lv === 0 || lv === 1 ? 1.5 : 1
    ctx.beginPath()
    ctx.moveTo(x1, y)
    ctx.lineTo(canvasWidth - 4, y)
    ctx.stroke()
    // Label kiri (level %)
    ctx.fillStyle = baseColor
    ctx.textAlign = 'left'
    ctx.fillText(`${(lv * 100).toFixed(1)}%`, x1 + 4, y - 6)
    // Label kanan (price)
    ctx.textAlign = 'right'
    ctx.fillText(price.toFixed(5), canvasWidth - 6, y - 6)
  }

  // Connecting trendline antara A dan B (orientation)
  ctx.setLineDash([3, 3])
  ctx.strokeStyle = baseColor + '66'
  ctx.lineWidth = 1
  ctx.beginPath()
  ctx.moveTo(pa.x, pa.y)
  ctx.lineTo(pb.x, pb.y)
  ctx.stroke()
  ctx.setLineDash([])
}

/**
 * Render Fibonacci extension/projection: anchor di A→B (swing), projection
 * di-attach ke C. Level standar 1.272, 1.618, 2.0, 2.618.
 */
function drawFibExtension(
  ctx: CanvasRenderingContext2D,
  a: DrawingPoint,
  b: DrawingPoint,
  c: DrawingPoint,
  dataToPixel: (p: DrawingPoint) => { x: number; y: number } | null,
  canvasWidth: number,
  baseColor: string,
) {
  const pa = dataToPixel(a)
  const pb = dataToPixel(b)
  const pc = dataToPixel(c)
  if (!pa || !pb || !pc) return

  const aPrice = a.price
  const bPrice = b.price
  const cPrice = c.price
  const range = bPrice - aPrice
  if (range === 0) return

  // Trendline A→B→C (orientation)
  ctx.setLineDash([3, 3])
  ctx.strokeStyle = baseColor + '66'
  ctx.lineWidth = 1
  ctx.beginPath()
  ctx.moveTo(pa.x, pa.y)
  ctx.lineTo(pb.x, pb.y)
  ctx.lineTo(pc.x, pc.y)
  ctx.stroke()
  ctx.setLineDash([])

  ctx.font = '10px ui-monospace, SFMono-Regular, monospace'
  ctx.textBaseline = 'middle'

  for (const lv of FIB_EXTENSION_LEVELS) {
    const projPrice = cPrice + range * lv
    // Pixel: pa.y untuk price=aPrice, pb.y untuk price=bPrice (linear interp)
    const projY = pc.y + (projPrice - cPrice) / range * (pb.y - pa.y)

    ctx.strokeStyle = baseColor + (lv === 1.618 || lv === 1 ? 'ee' : '88')
    ctx.lineWidth = lv === 1.618 ? 1.5 : 1
    ctx.beginPath()
    ctx.moveTo(pc.x, projY)
    ctx.lineTo(canvasWidth - 4, projY)
    ctx.stroke()

    ctx.fillStyle = baseColor
    ctx.textAlign = 'left'
    ctx.fillText(`${lv.toFixed(3)}`, pc.x + 4, projY - 6)
    ctx.textAlign = 'right'
    ctx.fillText(projPrice.toFixed(5), canvasWidth - 6, projY - 6)
  }
}
