'use client'

import { useEffect, useMemo, useRef, useState } from 'react'
import { Card, CardContent } from '@/components/ui/card'
import { PositionCard } from '@/components/dashboard/PositionCard'
import type { PositionCardData } from '@/lib/types'

interface PositionsListProps {
  positions: PositionCardData[] | null
  currentPrice?: number
  onCloseMarket?: (tradeId: string) => Promise<void>
}

const HISTORY_PAGE_SIZE = 10

export function PositionsList({ positions, currentPrice, onCloseMarket }: PositionsListProps) {
  const active = positions?.filter((p) => p.status === 'ACTIVE') ?? []
  // History: terbaru di atas (sort by closedAt desc, fallback ke tradeId desc)
  const closed = useMemo(() =>
    (positions?.filter((p) => p.status !== 'ACTIVE') ?? [])
      .slice()
      .sort((a, b) => {
        const ta = a.closedAt ? new Date(a.closedAt).getTime() : 0
        const tb = b.closedAt ? new Date(b.closedAt).getTime() : 0
        if (tb !== ta) return tb - ta
        return b.tradeId.localeCompare(a.tradeId)
      }),
    [positions]
  )

  // Infinite scroll: default 10, IntersectionObserver pada sentinel → +10 saat sentinel visible
  const [visibleCount, setVisibleCount] = useState(HISTORY_PAGE_SIZE)
  const closedLenRef = useRef(0)
  const scrollRootRef = useRef<HTMLDivElement | null>(null)
  useEffect(() => { closedLenRef.current = closed.length }, [closed.length])

  // Reset saat positions list mengecil (mis. clear history)
  useEffect(() => {
    if (visibleCount > closed.length) setVisibleCount(Math.min(HISTORY_PAGE_SIZE, closed.length))
  }, [closed.length, visibleCount])

  // Callback ref pattern: observer attach SEKALI saat sentinel mount/unmount, bukan setiap state change.
  // Hindari race re-attach yang fire false-positive saat positions auto-refresh.
  const sentinelCb = useRef<((node: HTMLDivElement | null) => void) | null>(null)
  if (sentinelCb.current === null) {
    let observer: IntersectionObserver | null = null
    sentinelCb.current = (node) => {
      if (observer) { observer.disconnect(); observer = null }
      if (!node) return
      observer = new IntersectionObserver((entries) => {
        if (entries[0]?.isIntersecting) {
          setVisibleCount((prev) => Math.min(prev + HISTORY_PAGE_SIZE, closedLenRef.current))
        }
      }, {
        root: scrollRootRef.current,  // scroll dalam container ini, bukan whole page
        rootMargin: '0px',
        threshold: 0.1,
      })
      observer.observe(node)
    }
  }

  if (!positions || positions.length === 0) {
    return (
      <Card className="opacity-50">
        <CardContent className="p-4 text-center">
          <span className="text-sm text-muted-foreground">No positions yet</span>
        </CardContent>
      </Card>
    )
  }

  return (
    <div className="space-y-2">
      {active.length > 0 && (
        <>
          <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground px-1">
            Open Positions ({active.length}/3)
          </p>
          {active.map((position) => (
            <PositionCard
              key={position.tradeId}
              position={position}
              currentPrice={currentPrice}
              onCloseMarket={onCloseMarket}
            />
          ))}
        </>
      )}
      {closed.length > 0 && (
        <>
          <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground px-1 pt-2">
            History ({closed.length})
          </p>
          {/* Scroll container — max-h-96 (~24rem ≈ 4 cards) supaya history tidak extend dashboard */}
          <div
            ref={scrollRootRef}
            className="max-h-96 overflow-y-auto space-y-2 pr-1"
          >
            {closed.slice(0, visibleCount).map((position) => (
              <PositionCard key={position.tradeId} position={position} />
            ))}
            {visibleCount < closed.length && (
              <div
                ref={sentinelCb.current}
                className="text-center text-xs text-muted-foreground py-2"
              >
                Loading more… ({visibleCount}/{closed.length})
              </div>
            )}
          </div>
        </>
      )}
    </div>
  )
}
