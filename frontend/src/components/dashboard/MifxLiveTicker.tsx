'use client'

import { cn } from '@/lib/utils'
import type { MifxStatusResponse } from '@/lib/types'

interface Props {
  status: MifxStatusResponse | null
}

export function MifxLiveTicker({ status }: Props) {
  const connected = status?.connected === true

  if (!connected) {
    return (
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <span className="w-2 h-2 rounded-full bg-muted-foreground/40 inline-block" />
        MT5 offline
      </div>
    )
  }

  return (
    <div className="flex items-center gap-3 text-xs">
      {/* Live indicator */}
      <div className="flex items-center gap-1.5">
        <span className="relative flex h-2 w-2">
          <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75" />
          <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-500" />
        </span>
        <span className="font-semibold text-emerald-500">MT5 LIVE</span>
      </div>

      {/* Bid */}
      <div className="flex items-center gap-1">
        <span className="text-muted-foreground">Bid</span>
        <span className={cn('font-mono font-bold tabular-nums')}>
          {status?.bid?.toFixed(5) ?? '—'}
        </span>
      </div>

      {/* Ask */}
      <div className="flex items-center gap-1">
        <span className="text-muted-foreground">Ask</span>
        <span className="font-mono font-bold tabular-nums">
          {status?.ask?.toFixed(5) ?? '—'}
        </span>
      </div>

      {/* Spread */}
      {status?.spreadPips != null && (
        <div className="text-muted-foreground">
          spread <span className="font-mono">{status.spreadPips.toFixed(1)}p</span>
        </div>
      )}

      {/* Timestamp */}
      {status?.time && (
        <span className="text-muted-foreground hidden sm:inline">
          {new Date(status.time).toLocaleTimeString('id-ID', {
            hour: '2-digit', minute: '2-digit', second: '2-digit'
          })}
        </span>
      )}
    </div>
  )
}
