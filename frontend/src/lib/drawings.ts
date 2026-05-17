/**
 * Drawing tool primitives untuk chart annotation (TradingView-style).
 *
 * Semua drawing tersimpan di localStorage per kombinasi `{pair, timeframe}`,
 * disimpan dalam koordinat data (time + price) — bukan pixel — supaya tetap
 * relevan saat user zoom/pan/ganti timeframe.
 */

export type DrawingType =
  | 'hline'
  | 'trendline'
  | 'rectangle'
  | 'ray'
  | 'text'
  | 'measure'
  | 'fib-retracement'
  | 'fib-extension'

/** Level retracement standar (0 = high anchor, 1 = low anchor untuk uptrend). */
export const FIB_RETRACEMENT_LEVELS = [0, 0.236, 0.382, 0.5, 0.618, 0.786, 1] as const

/** Level extension/projection — 0 = A, 1 = C, lalu projection 1.272/1.618/2.0/2.618. */
export const FIB_EXTENSION_LEVELS = [0, 0.618, 1, 1.272, 1.618, 2, 2.618] as const

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
  points: DrawingPoint[]   // hline/text: 1, trendline/rectangle/ray/measure: 2
  style: DrawingStyle
  createdAt: string        // ISO timestamp
  text?: string            // untuk type === 'text' (annotation content)
}

export const DEFAULT_STYLES: Record<DrawingType, DrawingStyle> = {
  hline:             { color: '#fbbf24', width: 1, dashed: true },   // amber
  trendline:         { color: '#60a5fa', width: 2 },                 // blue
  rectangle:         { color: 'rgba(168, 85, 247, 0.5)', width: 1 }, // purple translucent
  ray:               { color: '#a3e635', width: 2 },                 // lime — extend infinity
  text:              { color: '#e5e7eb', width: 1 },                 // gray-200 — label
  measure:           { color: 'rgba(96, 165, 250, 0.6)', width: 1 }, // blue translucent
  'fib-retracement': { color: '#f59e0b', width: 1 },                 // amber — retracement levels
  'fib-extension':   { color: '#06b6d4', width: 1 },                 // cyan — extension/projection
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

const HIT_THRESHOLD = 6

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
  if (drawing.type === 'hline') {
    const c = coords(drawing.points[0])
    if (!c) return false
    return Math.abs(py - c.y) <= HIT_THRESHOLD
  }
  if (drawing.type === 'trendline' || drawing.type === 'measure') {
    const a = coords(drawing.points[0])
    const b = coords(drawing.points[1])
    if (!a || !b) return false
    return distanceToSegment(px, py, a.x, a.y, b.x, b.y) <= HIT_THRESHOLD
  }
  if (drawing.type === 'ray') {
    const a = coords(drawing.points[0])
    const b = coords(drawing.points[1])
    if (!a || !b) return false
    // Ray: extend dari A melewati B hingga ujung kanvas
    return distanceToSegment(px, py, a.x, a.y, b.x, b.y) <= HIT_THRESHOLD
  }
  if (drawing.type === 'rectangle') {
    const a = coords(drawing.points[0])
    const b = coords(drawing.points[1])
    if (!a || !b) return false
    const x1 = Math.min(a.x, b.x), x2 = Math.max(a.x, b.x)
    const y1 = Math.min(a.y, b.y), y2 = Math.max(a.y, b.y)
    const onLeft  = Math.abs(px - x1) <= HIT_THRESHOLD && py >= y1 && py <= y2
    const onRight = Math.abs(px - x2) <= HIT_THRESHOLD && py >= y1 && py <= y2
    const onTop   = Math.abs(py - y1) <= HIT_THRESHOLD && px >= x1 && px <= x2
    const onBot   = Math.abs(py - y2) <= HIT_THRESHOLD && px >= x1 && px <= x2
    return onLeft || onRight || onTop || onBot
  }
  if (drawing.type === 'text') {
    const c = coords(drawing.points[0])
    if (!c) return false
    const len = (drawing.text ?? '').length
    const w = Math.max(20, len * 6)
    return px >= c.x - 2 && px <= c.x + w + 2 && py >= c.y - 10 && py <= c.y + 6
  }
  if (drawing.type === 'fib-retracement') {
    const a = coords(drawing.points[0])
    const b = coords(drawing.points[1])
    if (!a || !b) return false
    // Hit kalau klik dekat salah satu level line
    const x1 = Math.min(a.x, b.x), x2 = Math.max(a.x, b.x)
    if (px < x1 || px > x2) return false
    for (const lv of FIB_RETRACEMENT_LEVELS) {
      const y = a.y + (b.y - a.y) * lv
      if (Math.abs(py - y) <= HIT_THRESHOLD) return true
    }
    return false
  }
  if (drawing.type === 'fib-extension') {
    const a = coords(drawing.points[0])
    const b = coords(drawing.points[1])
    const c = coords(drawing.points[2])
    if (!a || !b || !c) return false
    // Extension: anchor di C, projection berdasarkan (B - A)
    const minX = Math.min(a.x, b.x, c.x)
    const maxX = Math.max(a.x, b.x, c.x)
    if (px < minX || px > maxX + 200) return false  // tolerance untuk projection ke kanan
    const aPrice = drawing.points[0].price
    const bPrice = drawing.points[1].price
    const cPrice = drawing.points[2].price
    const range = bPrice - aPrice
    for (const lv of FIB_EXTENSION_LEVELS) {
      const projPrice = cPrice + range * lv
      const projY = c.y + (projPrice - cPrice) / (range || 1) * (b.y - a.y)
      if (Math.abs(py - projY) <= HIT_THRESHOLD) return true
    }
    return false
  }
  return false
}

/**
 * Snap data point ke candle terdekat (by time) — pilih high/low/close
 * berdasarkan mana yang paling dekat dengan price yang di-klik.
 * Magnet mode untuk Fib retracement / extension supaya endpoint tepat
 * di swing high/low candle.
 */
export function snapToCandle(
  point: DrawingPoint,
  candles: Array<{ time: number; high: number; low: number; close: number; open: number }>,
): DrawingPoint {
  if (candles.length === 0) return point
  // Cari candle dengan time paling dekat
  let nearest = candles[0]
  let minDt = Math.abs(candles[0].time - point.time)
  for (const c of candles) {
    const dt = Math.abs(c.time - point.time)
    if (dt < minDt) { minDt = dt; nearest = c }
  }
  // Pilih level (high/low/close/open) yang paling dekat dengan price klik
  const candidates = [
    { label: 'high',  price: nearest.high  },
    { label: 'low',   price: nearest.low   },
    { label: 'close', price: nearest.close },
    { label: 'open',  price: nearest.open  },
  ]
  let best = candidates[0]
  let minDp = Math.abs(best.price - point.price)
  for (const cand of candidates) {
    const dp = Math.abs(cand.price - point.price)
    if (dp < minDp) { minDp = dp; best = cand }
  }
  return { time: nearest.time, price: best.price }
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
