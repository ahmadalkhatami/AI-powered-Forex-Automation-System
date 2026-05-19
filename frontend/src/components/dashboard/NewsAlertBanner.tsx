'use client'

import { useEffect, useState } from 'react'
import { fetchNews } from '@/lib/api'
import type { NewsEventDto } from '@/lib/types'

/**
 * Banner showing imminent high-impact news events (EUR/USD focused).
 * Hidden kalau no event dalam next 60 min. Visible saat upcoming high-impact event.
 *
 * <para>Display logic:</para>
 * <list type="bullet">
 *   <item>Within next 60 min: red banner (DANGER — avoid trade)</item>
 *   <item>Within next 4 hours: amber banner (CAUTION — be aware)</item>
 *   <item>None imminent: tampilkan ringkasan next event terdekat kalau dalam 24h</item>
 * </list>
 */
export function NewsAlertBanner() {
  const [events, setEvents] = useState<NewsEventDto[]>([])
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    const load = async () => {
      try {
        const r = await fetchNews(24, 'USD,EUR')
        if (alive) {
          setEvents(r.events ?? [])
          setError(r.fetchError ?? null)
        }
      } catch (e) {
        if (alive) setError(e instanceof Error ? e.message : 'Unknown error')
      }
    }
    load()
    const id = setInterval(load, 5 * 60_000)  // refresh tiap 5 menit
    return () => { alive = false; clearInterval(id) }
  }, [])

  // Only high-impact events matter for trading halt logic
  const highImpactUpcoming = events.filter((e) =>
    e.impact === 'High' && e.minutesUntil >= -30 && e.minutesUntil <= 240)

  // Pick the most imminent
  const imminent = highImpactUpcoming
    .filter((e) => e.minutesUntil >= -30 && e.minutesUntil <= 60)
    .sort((a, b) => a.minutesUntil - b.minutesUntil)[0]

  const upcoming = !imminent
    ? highImpactUpcoming
        .filter((e) => e.minutesUntil > 60 && e.minutesUntil <= 240)
        .sort((a, b) => a.minutesUntil - b.minutesUntil)[0]
    : null

  if (error && !imminent && !upcoming) {
    // Silent — news feed down, jangan distrupt user.
    return null
  }

  if (imminent) {
    const isPast = imminent.minutesUntil < 0
    return (
      <div className="rounded-md border border-red-500/50 bg-red-500/10 px-3 py-2 text-xs flex items-center gap-2">
        <span className="text-red-500 font-bold">⚠ HIGH IMPACT</span>
        <span className="font-mono">{imminent.currency}</span>
        <span className="truncate flex-1">{imminent.title}</span>
        <span className="text-red-500 font-mono font-semibold whitespace-nowrap">
          {isPast ? `${Math.abs(imminent.minutesUntil)} min ago` : `in ${imminent.minutesUntil} min`}
        </span>
      </div>
    )
  }

  if (upcoming) {
    return (
      <div className="rounded-md border border-amber-500/40 bg-amber-500/5 px-3 py-2 text-xs flex items-center gap-2">
        <span className="text-amber-500">📅 Upcoming</span>
        <span className="font-mono">{upcoming.currency}</span>
        <span className="truncate flex-1 text-muted-foreground">{upcoming.title}</span>
        <span className="text-amber-500 font-mono whitespace-nowrap">
          in {Math.floor(upcoming.minutesUntil / 60)}h {upcoming.minutesUntil % 60}m
        </span>
      </div>
    )
  }

  return null
}
