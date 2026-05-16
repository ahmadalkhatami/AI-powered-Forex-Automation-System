'use client'

import { useEffect, useMemo, useRef, useState } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { RefreshCw } from 'lucide-react'
import { cn } from '@/lib/utils'
import { getAuditEvents } from '@/lib/api'
import type { AuditEvent } from '@/lib/types'

const PAGE_SIZE = 30

const TYPES: { value: string | null; label: string }[] = [
  { value: null,       label: 'All'      },
  { value: 'signal',   label: 'Signal'   },
  { value: 'risk',     label: 'Risk'     },
  { value: 'execute',  label: 'Execute'  },
  { value: 'close',    label: 'Close'    },
  { value: 'block',    label: 'Blocked'  },
  { value: 'halt',     label: 'Halt'     },
  { value: 'resume',   label: 'Resume'   },
]

const TYPE_COLOR: Record<string, string> = {
  signal:  'bg-blue-500/15 text-blue-600 dark:text-blue-400 border-blue-500/30',
  risk:    'bg-purple-500/15 text-purple-600 dark:text-purple-400 border-purple-500/30',
  execute: 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400 border-emerald-500/30',
  close:   'bg-amber-500/15 text-amber-600 dark:text-amber-400 border-amber-500/30',
  block:   'bg-orange-500/15 text-orange-600 dark:text-orange-400 border-orange-500/30',
  halt:    'bg-red-500/15 text-red-600 dark:text-red-400 border-red-500/30',
  resume:  'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400 border-emerald-500/30',
}

export default function AuditPage() {
  const [events, setEvents] = useState<AuditEvent[]>([])
  const [loading, setLoading] = useState(true)
  const [filter, setFilter] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<number | null>(null)
  const [visibleCount, setVisibleCount] = useState(PAGE_SIZE)

  // Sort newest-first di frontend — deterministic, tidak depend backend ordering.
  const sortedEvents = useMemo(() =>
    events.slice().sort((a, b) =>
      new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime()
    ),
    [events]
  )

  const eventsLenRef = useRef(0)
  useEffect(() => { eventsLenRef.current = sortedEvents.length }, [sortedEvents.length])

  const load = async () => {
    setLoading(true)
    try {
      const data = await getAuditEvents(500, filter ?? undefined)
      setEvents(data)
    } catch {
      setEvents([])
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    setVisibleCount(PAGE_SIZE)  // reset saat filter berubah
    load()
    const id = setInterval(load, 5000)
    return () => clearInterval(id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filter])

  // Infinite scroll: window scroll listener — load batch berikutnya hanya saat user
  // mencapai dekat bottom page. Throttle via rAF. Lebih predictable dari IntersectionObserver
  // yang punya edge case re-attach race + initial callback firing.
  useEffect(() => {
    let raf: number | null = null
    const onScroll = () => {
      if (raf !== null) return
      raf = requestAnimationFrame(() => {
        raf = null
        const scrollY = window.scrollY
        const viewport = window.innerHeight
        const docHeight = document.documentElement.scrollHeight
        // Trigger saat 100px sebelum bottom page
        if (scrollY + viewport >= docHeight - 100) {
          setVisibleCount((prev) => {
            if (prev >= eventsLenRef.current) return prev
            return Math.min(prev + PAGE_SIZE, eventsLenRef.current)
          })
        }
      })
    }
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => {
      window.removeEventListener('scroll', onScroll)
      if (raf !== null) cancelAnimationFrame(raf)
    }
  }, [])

  return (
    <div className="space-y-4 max-w-5xl mx-auto">
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <h1 className="text-xl font-semibold">Audit Log</h1>
        <Button variant="outline" size="sm" onClick={load} className="gap-2">
          <RefreshCw className={cn('h-3.5 w-3.5', loading && 'animate-spin')} />
          Refresh
        </Button>
      </div>

      <div className="flex flex-wrap gap-1">
        {TYPES.map((t) => (
          <button
            key={t.label}
            onClick={() => setFilter(t.value)}
            className={cn(
              'px-2.5 py-1 text-xs font-mono rounded border transition-colors',
              filter === t.value
                ? 'bg-primary/15 border-primary/40 text-primary'
                : 'border-border/40 text-muted-foreground hover:text-foreground hover:border-border',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      <Card>
        <CardHeader className="pb-2 pt-3 px-4">
          <CardTitle className="text-sm">
            {events.length} events · showing {Math.min(visibleCount, events.length)} · auto-refresh tiap 5s
          </CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {events.length === 0 ? (
            <div className="p-6 text-center text-sm text-muted-foreground">
              {loading ? 'Loading…' : 'Belum ada events. Run analisis untuk mulai populate audit log.'}
            </div>
          ) : (
            <div className="divide-y divide-border/50">
              {sortedEvents.slice(0, visibleCount).map((evt, idx) => {
                const isOpen = expanded === idx
                return (
                  <div key={idx} className="px-3 py-2">
                    <button
                      onClick={() => setExpanded(isOpen ? null : idx)}
                      className="w-full flex items-start gap-3 text-left hover:bg-muted/30 -mx-3 px-3 py-1 rounded transition-colors"
                    >
                      <span className="text-[10px] font-mono text-muted-foreground whitespace-nowrap mt-0.5">
                        {new Date(evt.timestamp).toLocaleString('id-ID', {
                          day: '2-digit',
                          month: 'short',
                          hour: '2-digit',
                          minute: '2-digit',
                          second: '2-digit',
                        })}
                      </span>
                      <span
                        className={cn(
                          'px-1.5 py-0.5 rounded text-[10px] font-mono font-bold uppercase border whitespace-nowrap',
                          TYPE_COLOR[evt.type] ?? 'bg-muted text-muted-foreground border-border/40',
                        )}
                      >
                        {evt.type}
                      </span>
                      <span className="text-xs flex-1 break-words">{evt.summary}</span>
                    </button>
                    {isOpen && evt.payload !== null && evt.payload !== undefined && (
                      <pre className="ml-[150px] mt-2 text-[10px] font-mono bg-muted/30 p-2 rounded overflow-x-auto">
                        {JSON.stringify(evt.payload, null, 2)}
                      </pre>
                    )}
                  </div>
                )
              })}
              {visibleCount < events.length && (
                <div className="text-center text-xs text-muted-foreground py-3">
                  Scroll untuk load more… ({visibleCount}/{events.length})
                </div>
              )}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
