'use client'

import { useEffect, useState } from 'react'
import { Globe } from 'lucide-react'
import { cn } from '@/lib/utils'

interface SessionInfo {
  name: string
  color: string  // tailwind classes
  description: string
}

/**
 * Detect session aktif berdasarkan UTC hour — mirror backend DetectSession.
 * Weekend (Sat/Sun) → Closed.
 */
function detectSession(now: Date): SessionInfo {
  // Weekend check (UTC)
  const day = now.getUTCDay()  // 0=Sun, 6=Sat
  if (day === 0 || day === 6) {
    return {
      name: 'Closed',
      color: 'border-zinc-500/40 bg-zinc-500/10 text-zinc-400',
      description: 'Weekend — market closed',
    }
  }

  const h = now.getUTCHours()
  const london  = h >= 7 && h < 16
  const newYork = h >= 12 && h < 21
  const tokyo   = h >= 0 && h < 9
  const sydney  = h >= 21 || h < 6

  if (london && newYork) {
    return {
      name: 'London/NY',
      color: 'border-violet-500/40 bg-violet-500/10 text-violet-400',
      description: 'Overlap session — highest liquidity',
    }
  }
  if (london) {
    return {
      name: 'London',
      color: 'border-blue-500/40 bg-blue-500/10 text-blue-400',
      description: 'London session',
    }
  }
  if (newYork) {
    return {
      name: 'New York',
      color: 'border-purple-500/40 bg-purple-500/10 text-purple-400',
      description: 'New York session',
    }
  }
  if (tokyo) {
    return {
      name: 'Tokyo',
      color: 'border-rose-500/40 bg-rose-500/10 text-rose-400',
      description: 'Tokyo session',
    }
  }
  if (sydney) {
    return {
      name: 'Sydney',
      color: 'border-cyan-500/40 bg-cyan-500/10 text-cyan-400',
      description: 'Sydney session',
    }
  }
  return {
    name: 'Closed',
    color: 'border-zinc-500/40 bg-zinc-500/10 text-zinc-400',
    description: 'Market gap',
  }
}

function formatLocalTime(d: Date): string {
  return new Intl.DateTimeFormat('id-ID', {
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'Asia/Jakarta',
  }).format(d)
}

/**
 * Display current trading session + local time WIB. Auto-update tiap 60 detik.
 */
export function SessionChip() {
  const [now, setNow] = useState<Date>(() => new Date())

  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 60_000)
    return () => clearInterval(id)
  }, [])

  const session = detectSession(now)
  const localTime = formatLocalTime(now)

  return (
    <div
      title={session.description}
      className={cn(
        'flex items-center gap-1.5 px-2.5 py-1 rounded-md border text-xs font-medium',
        session.color,
      )}
    >
      <Globe size={12} />
      <span>{session.name}</span>
      <span className="text-muted-foreground font-mono">·</span>
      <span className="font-mono">{localTime}</span>
      <span className="text-muted-foreground text-[10px]">WIB</span>
    </div>
  )
}
