/**
 * Drawing tool primitives untuk chart annotation (TradingView-style).
 *
 * Semua drawing tersimpan di localStorage per kombinasi `{pair, timeframe}`,
 * disimpan dalam koordinat data (time + price) — bukan pixel — supaya tetap
 * relevan saat user zoom/pan/ganti timeframe.
 */

export type DrawingType = 'hline' | 'trendline' | 'rectangle'

export interface DrawingPoint {
  time: number   // Unix timestamp seconds (sesuai lightweight-charts)
  price: number
}

export interface DrawingStyle {
  color: string
  width: number   // line width in px
  dashed?: boolean
}

export interface Drawing {
  id: string
  type: DrawingType
  points: DrawingPoint[]   // hline: 1 point (cuma price dipakai), trendline: 2, rectangle: 2 (diagonal)
  style: DrawingStyle
  createdAt: string        // ISO timestamp
}

export const DEFAULT_STYLES: Record<DrawingType, DrawingStyle> = {
  hline:     { color: '#fbbf24', width: 1, dashed: true },   // amber
  trendline: { color: '#60a5fa', width: 2 },                 // blue
  rectangle: { color: 'rgba(168, 85, 247, 0.5)', width: 1 }, // purple translucent
}

const STORAGE_PREFIX = 'forexai.drawings'

function storageKey(pair: string, timeframe: string): string {
  return `${STORAGE_PREFIX}.${pair.replace(/\//g, '')}.${timeframe}`
}

export function loadDrawings(pair: string, timeframe: string): Drawing[] {
  if (typeof window === 'undefined') return []
  try {
    const raw = localStorage.getItem(storageKey(pair, timeframe))
    if (!raw) return []
    const parsed = JSON.parse(raw)
    if (!Array.isArray(parsed)) return []
    return parsed.filter(isValidDrawing)
  } catch {
    return []
  }
}

export function saveDrawings(pair: string, timeframe: string, drawings: Drawing[]): void {
  if (typeof window === 'undefined') return
  try {
    localStorage.setItem(storageKey(pair, timeframe), JSON.stringify(drawings))
  } catch {
    // Quota exceeded atau localStorage blocked — fail silent
  }
}

function isValidDrawing(d: unknown): d is Drawing {
  if (!d || typeof d !== 'object') return false
  const x = d as Record<string, unknown>
  return (
    typeof x.id === 'string' &&
    typeof x.type === 'string' &&
    Array.isArray(x.points) &&
    x.points.length > 0 &&
    typeof x.style === 'object'
  )
}

export function makeId(): string {
  return `dw_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`
}

/**
 * Hit-test: cek apakah pixel (x, y) berada dekat sebuah drawing.
 * Threshold 6px (mudah di-klik tapi tidak terlalu sensitif).
 */
export function hitTest(
  drawing: Drawing,
  px: number,
  py: number,
  coords: (point: DrawingPoint) => { x: number; y: number } | null,
): boolean {
  const HIT_THRESHOLD = 6
  if (drawing.type === 'hline') {
    const c = coords(drawing.points[0])
    if (!c) return false
    return Math.abs(py - c.y) <= HIT_THRESHOLD
  }
  if (drawing.type === 'trendline') {
    const a = coords(drawing.points[0])
    const b = coords(drawing.points[1])
    if (!a || !b) return false
    return distanceToSegment(px, py, a.x, a.y, b.x, b.y) <= HIT_THRESHOLD
  }
  if (drawing.type === 'rectangle') {
    const a = coords(drawing.points[0])
    const b = coords(drawing.points[1])
    if (!a || !b) return false
    const x1 = Math.min(a.x, b.x), x2 = Math.max(a.x, b.x)
    const y1 = Math.min(a.y, b.y), y2 = Math.max(a.y, b.y)
    // Hit kalau di-klik tepat di border (4 sisi)
    const onLeft  = Math.abs(px - x1) <= HIT_THRESHOLD && py >= y1 && py <= y2
    const onRight = Math.abs(px - x2) <= HIT_THRESHOLD && py >= y1 && py <= y2
    const onTop   = Math.abs(py - y1) <= HIT_THRESHOLD && px >= x1 && px <= x2
    const onBot   = Math.abs(py - y2) <= HIT_THRESHOLD && px >= x1 && px <= x2
    return onLeft || onRight || onTop || onBot
  }
  return false
}

function distanceToSegment(
  px: number, py: number,
  ax: number, ay: number,
  bx: number, by: number,
): number {
  const dx = bx - ax
  const dy = by - ay
  const lenSq = dx * dx + dy * dy
  if (lenSq === 0) return Math.hypot(px - ax, py - ay)
  let t = ((px - ax) * dx + (py - ay) * dy) / lenSq
  t = Math.max(0, Math.min(1, t))
  const cx = ax + t * dx
  const cy = ay + t * dy
  return Math.hypot(px - cx, py - cy)
}
