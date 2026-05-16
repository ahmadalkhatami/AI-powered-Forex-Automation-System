'use client'

import { useCallback, useEffect, useRef, useState } from 'react'
import type { IChartApi, ISeriesApi, Time, UTCTimestamp } from 'lightweight-charts'
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
 * Koordinat: drawing disimpan dalam data space (time + price), di-render
 * ke pixel via chart API. Saat user zoom/pan, kita subscribe ke range
 * change events dan trigger redraw.
 *
 * Pointer events:
 *  - activeTool === 'cursor': pointer-events:none → chart terima mouse (zoom/pan normal)
 *  - activeTool === drawing tool: pointer-events:auto → kita capture clicks
 *  - Klik dengan tool aktif: track drawing creation (1 or 2 click depending on tool)
 *  - Klik dengan cursor di drawing existing: select (handled in parent)
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

  // Konversi data point → pixel coord (return null kalau di luar visible range)
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
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    } else {
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    }
    ctx.clearRect(0, 0, width, height)

    for (const d of drawings) {
      drawShape(ctx, d, dataToPixel, width, d.id === selectedId)
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
        } else if (activeTool === 'rectangle') {
          ctx.strokeRect(
            Math.min(start.x, hoverPixel.x),
            Math.min(start.y, hoverPixel.y),
            Math.abs(hoverPixel.x - start.x),
            Math.abs(hoverPixel.y - start.y),
          )
        }
        ctx.setLineDash([])
      }
    }
  }, [drawings, dataToPixel, width, height, selectedId, pendingPoint, hoverPixel, activeTool])

  // Subscribe ke chart events untuk trigger redraw
  useEffect(() => {
    if (!chart) return
    const ts = chart.timeScale()
    const handler = () => render()
    ts.subscribeVisibleLogicalRangeChange(handler)
    return () => {
      ts.unsubscribeVisibleLogicalRangeChange(handler)
    }
  }, [chart, render])

  // Redraw saat drawings/width/height/preview berubah
  useEffect(() => {
    render()
  }, [render])

  // Mouse interactions
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

      // Drawing tool active
      const dataPoint = pixelToData(px, py)
      if (!dataPoint) return

      if (activeTool === 'hline') {
        // 1-click tool: langsung buat hline
        const newDrawing: Drawing = {
          id: makeId(),
          type: 'hline',
          points: [dataPoint],
          style: { ...DEFAULT_STYLES.hline },
          createdAt: new Date().toISOString(),
        }
        onDrawingsChange([...drawings, newDrawing])
        return
      }

      // 2-click tools: trendline, rectangle
      if (!pendingPoint) {
        setPendingPoint(dataPoint)
      } else {
        const newDrawing: Drawing = {
          id: makeId(),
          type: activeTool as DrawingType,
          points: [pendingPoint, dataPoint],
          style: { ...DEFAULT_STYLES[activeTool as DrawingType] },
          createdAt: new Date().toISOString(),
        }
        onDrawingsChange([...drawings, newDrawing])
        setPendingPoint(null)
        setHoverPixel(null)
      }
    },
    [activeTool, drawings, dataToPixel, pixelToData, onDrawingsChange, pendingPoint, onSelectedIdChange],
  )

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

  // Penting: pointer-events HARUS none di cursor mode supaya chart bisa pan/zoom normal.
  // Individual drawing select-delete di-defer ke Stage 2 (perlu pointer event forwarding).
  // Untuk Stage 1, delete pakai "Clear all" di toolbar.
  const pointerActive = activeTool !== 'cursor'
  const cursorStyle = activeTool === 'cursor' ? 'default' : 'crosshair'

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

function drawShape(
  ctx: CanvasRenderingContext2D,
  d: Drawing,
  dataToPixel: (p: DrawingPoint) => { x: number; y: number } | null,
  canvasWidth: number,
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
    // Price label di sebelah kanan
    const priceLabel = d.points[0].price.toFixed(5)
    ctx.setLineDash([])
    ctx.fillStyle = d.style.color
    ctx.font = '10px ui-monospace, SFMono-Regular, monospace'
    ctx.textAlign = 'right'
    ctx.textBaseline = 'middle'
    ctx.fillText(priceLabel, canvasWidth - 6, a.y - 6)
  } else if (d.type === 'trendline') {
    const a = dataToPixel(d.points[0])
    const b = dataToPixel(d.points[1])
    if (!a || !b) return
    ctx.beginPath()
    ctx.moveTo(a.x, a.y)
    ctx.lineTo(b.x, b.y)
    ctx.stroke()
  } else if (d.type === 'rectangle') {
    const a = dataToPixel(d.points[0])
    const b = dataToPixel(d.points[1])
    if (!a || !b) return
    const x = Math.min(a.x, b.x)
    const y = Math.min(a.y, b.y)
    const w = Math.abs(b.x - a.x)
    const h = Math.abs(b.y - a.y)
    // Fill semi-transparan + border solid
    ctx.fillStyle = d.style.color.includes('rgba')
      ? d.style.color
      : d.style.color + '22'
    ctx.fillRect(x, y, w, h)
    ctx.setLineDash([])
    ctx.strokeRect(x, y, w, h)
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
