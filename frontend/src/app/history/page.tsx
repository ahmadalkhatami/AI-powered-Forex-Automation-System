'use client'

import { useState, useEffect } from 'react'
import Link from 'next/link'
import { ArrowLeft } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { getAllPositions } from '@/lib/api'
import type { TradePositionResponse } from '@/lib/types'

function formatDate(iso: string | null): string {
  if (!iso) return '—'
  return new Intl.DateTimeFormat('id-ID', {
    dateStyle: 'short',
    timeStyle: 'short',
    timeZone: 'Asia/Jakarta',
  }).format(new Date(iso))
}

export default function HistoryPage() {
  const [positions, setPositions] = useState<TradePositionResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getAllPositions()
      .then((data) => {
        setPositions(data)
        setLoading(false)
      })
      .catch(() => {
        setError('Could not reach backend API')
        setLoading(false)
      })
  }, [])

  const closed = positions.filter(
    (p) => p.status === 'CLOSED_WIN' || p.status === 'CLOSED_LOSS',
  )
  const wins = closed.filter((p) => p.status === 'CLOSED_WIN').length
  const losses = closed.filter((p) => p.status === 'CLOSED_LOSS').length
  const winRate = closed.length > 0 ? (wins / closed.length) * 100 : 0
  const totalPnl = closed.reduce((sum, p) => sum + p.floatingPnl, 0)

  return (
    <div className="space-y-6">
      {/* Back navigation */}
      <Link
        href="/"
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground transition-colors"
      >
        <ArrowLeft className="h-4 w-4" />
        Dashboard
      </Link>

      <h1 className="text-2xl font-bold tracking-tight">Trade History</h1>

      {loading && (
        <p className="text-sm text-muted-foreground">Loading history…</p>
      )}

      {error && (
        <p className="text-sm text-red-600">{error}</p>
      )}

      {!loading && !error && (
        <>
          {/* Summary stats */}
          <div className="grid grid-cols-2 sm:grid-cols-5 gap-3">
            {[
              { label: 'Total Trades', value: closed.length },
              { label: 'Wins', value: wins, color: 'text-emerald-600 dark:text-emerald-400' },
              { label: 'Losses', value: losses, color: 'text-red-600 dark:text-red-400' },
              {
                label: 'Win Rate',
                value: `${winRate.toFixed(1)}%`,
                color: winRate >= 50 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400',
              },
              {
                label: 'Total P&L',
                value: `${totalPnl >= 0 ? '+' : ''}$${totalPnl.toFixed(2)}`,
                color: totalPnl >= 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400',
              },
            ].map((stat) => (
              <Card key={stat.label}>
                <CardContent className="p-3">
                  <p className="text-xs text-muted-foreground">{stat.label}</p>
                  <p className={cn('font-mono font-bold text-lg', stat.color)}>
                    {stat.value}
                  </p>
                </CardContent>
              </Card>
            ))}
          </div>

          {/* Table or empty state */}
          {closed.length === 0 ? (
            <Card>
              <CardContent className="p-8 text-center">
                <p className="text-sm text-muted-foreground">
                  No trades yet — approve your first signal to see history here
                </p>
              </CardContent>
            </Card>
          ) : (
            <Card>
              <CardContent className="p-0 overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b text-left text-xs text-muted-foreground uppercase tracking-wider">
                      {['Trade ID', 'Pair', 'Dir', 'Entry', 'SL', 'TP', 'Lot', 'Risk $', 'P&L $', 'Result', 'Opened', 'Closed'].map(
                        (h) => (
                          <th key={h} className="px-4 py-3 font-medium whitespace-nowrap">
                            {h}
                          </th>
                        ),
                      )}
                    </tr>
                  </thead>
                  <tbody>
                    {closed.map((p) => {
                      const isWin = p.status === 'CLOSED_WIN'
                      const pnlClass = isWin ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400'
                      return (
                        <tr key={p.tradeId} className="border-b hover:bg-muted/30 transition-colors">
                          <td className="px-4 py-3 font-mono text-xs text-muted-foreground">
                            {p.tradeId}
                          </td>
                          <td className="px-4 py-3 font-semibold">{p.pair}</td>
                          <td className="px-4 py-3">
                            <Badge
                              variant="outline"
                              className={cn(
                                'text-xs font-bold',
                                p.direction === 'BUY'
                                  ? 'text-emerald-600 dark:text-emerald-400 border-emerald-300 dark:border-emerald-700'
                                  : 'text-red-600 dark:text-red-400 border-red-300 dark:border-red-700',
                              )}
                            >
                              {p.direction}
                            </Badge>
                          </td>
                          <td className="px-4 py-3 font-mono">{p.entry.toFixed(4)}</td>
                          <td className="px-4 py-3 font-mono text-red-600 dark:text-red-400">
                            {p.stopLoss.toFixed(4)}
                          </td>
                          <td className="px-4 py-3 font-mono text-emerald-600 dark:text-emerald-400">
                            {p.takeProfit.toFixed(4)}
                          </td>
                          <td className="px-4 py-3 font-mono">{p.lotSize.toFixed(2)}</td>
                          <td className="px-4 py-3 font-mono">${p.riskAmount.toFixed(2)}</td>
                          <td className={cn('px-4 py-3 font-mono font-bold', pnlClass)}>
                            {p.floatingPnl >= 0 ? '+' : ''}
                            {p.floatingPnl.toFixed(2)}
                          </td>
                          <td className={cn('px-4 py-3 font-semibold', pnlClass)}>
                            {isWin ? 'WIN' : 'LOSS'}
                          </td>
                          <td className="px-4 py-3 text-xs text-muted-foreground whitespace-nowrap">
                            {formatDate(p.openedAt)}
                          </td>
                          <td className="px-4 py-3 text-xs text-muted-foreground whitespace-nowrap">
                            {formatDate(p.closedAt)}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </CardContent>
            </Card>
          )}
        </>
      )}
    </div>
  )
}
