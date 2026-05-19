'use client'

import { cn } from '@/lib/utils'

interface MarketSnapshotLike {
  mA20_M15?: number
  mA50_M15?: number
  mA20_H1?: number
  mA50_H1?: number
  mA20_D1?: number
  mA50_D1?: number
}

interface Props {
  snapshot: MarketSnapshotLike | undefined | null
}

/**
 * Quick at-a-glance chip menampilkan arah trend per TF (M15/H1/D1) berdasarkan
 * MA20 vs MA50 cross. Bullish (MA20 > MA50) = arrow up, sebaliknya down.
 *
 * <para>Color logic:</para>
 * <list type="bullet">
 *   <item>Semua TF aligned bullish: emerald solid (strong uptrend confluence)</item>
 *   <item>Semua TF aligned bearish: red solid</item>
 *   <item>Mixed: amber (transitional, ambiguous bias)</item>
 *   <item>D1 unavailable (zero values): show "M15 H1" only, neutral tone</item>
 * </list>
 *
 * Hidden kalau snapshot belum ada (initial state sebelum signal analyze).
 */
export function MultiTfMaChip({ snapshot }: Props) {
  if (!snapshot) return null

  const m15 = direction(snapshot.mA20_M15, snapshot.mA50_M15)
  const h1  = direction(snapshot.mA20_H1, snapshot.mA50_H1)
  const d1  = direction(snapshot.mA20_D1, snapshot.mA50_D1)

  // Determine overall alignment color
  const valid = [m15, h1, d1].filter((d) => d !== null) as ('up' | 'down')[]
  if (valid.length === 0) return null

  const allUp   = valid.every((d) => d === 'up')
  const allDown = valid.every((d) => d === 'down')
  const tone    = allUp ? 'bull' : allDown ? 'bear' : 'mixed'

  const toneCls = tone === 'bull'
    ? 'bg-emerald-500/15 text-emerald-600 border-emerald-500/40 dark:text-emerald-400'
    : tone === 'bear'
      ? 'bg-red-500/15 text-red-600 border-red-500/40 dark:text-red-400'
      : 'bg-amber-500/10 text-amber-600 border-amber-500/40 dark:text-amber-400'

  return (
    <span
      className={cn(
        'px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-wide border font-mono',
        toneCls,
      )}
      title={`Multi-TF MA alignment: ${tone === 'bull' ? 'all bullish' : tone === 'bear' ? 'all bearish' : 'mixed'}`}
    >
      <Tf label="M15" dir={m15} />
      {' '}
      <Tf label="H1" dir={h1} />
      {' '}
      <Tf label="D1" dir={d1} />
    </span>
  )
}

function Tf({ label, dir }: { label: string; dir: 'up' | 'down' | null }) {
  if (dir === null) return <span className="opacity-50">{label}—</span>
  return <span>{label}{dir === 'up' ? '↑' : '↓'}</span>
}

function direction(ma20: number | undefined, ma50: number | undefined): 'up' | 'down' | null {
  if (!ma20 || !ma50 || ma20 <= 0 || ma50 <= 0) return null
  return ma20 > ma50 ? 'up' : 'down'
}
