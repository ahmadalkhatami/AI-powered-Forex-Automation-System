'use client'

import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { cn } from '@/lib/utils'
import type { ActionState, SignalHeroData } from '@/lib/types'

interface ApproveRejectActionsProps {
  state: ActionState
  signal: SignalHeroData
  mode?: 'MIFX_DEMO' | 'SIMULATION'
  onApprove?: () => void
  onReject?: () => void
}

export function ApproveRejectActions({
  state,
  signal,
  mode = 'SIMULATION',
  onApprove,
  onReject,
}: ApproveRejectActionsProps) {
  const [approveOpen, setApproveOpen] = useState(false)
  const [rejectOpen, setRejectOpen] = useState(false)

  const isProcessing = state === 'processing'

  return (
    <div className="flex flex-col gap-2">
      <div className="flex gap-3">
        {/* APPROVE — always left */}
        <Button
          className={cn(
            'flex-1 font-semibold',
            state === 'enabled-go' && 'bg-emerald-600 hover:bg-emerald-700 text-white',
            state === 'enabled-caution' && 'bg-amber-500 hover:bg-amber-600 text-white',
            state === 'disabled-nogo' && 'bg-emerald-600 text-white opacity-70',
            state === 'processing' && 'bg-emerald-600 text-white opacity-75 cursor-not-allowed pointer-events-none',
          )}
          aria-disabled={isProcessing}
          onClick={!isProcessing ? () => setApproveOpen(true) : undefined}
        >
          {isProcessing ? (
            <span className="flex items-center gap-2">
              <Loader2 size={16} className="animate-spin" />
              Processing…
            </span>
          ) : (
            'APPROVE'
          )}
        </Button>

        {/* REJECT — always right */}
        <Button
          variant="outline"
          className={cn(
            'flex-1 font-semibold border-red-300 dark:border-red-700 text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-950/30 hover:text-red-700 dark:hover:text-red-300',
            isProcessing && 'opacity-50 cursor-not-allowed pointer-events-none',
          )}
          onClick={!isProcessing ? () => setRejectOpen(true) : undefined}
          aria-disabled={isProcessing}
        >
          REJECT
        </Button>
      </div>

      {/* Caution notes below APPROVE when GO_WITH_CAUTION */}
      {state === 'enabled-caution' &&
        signal.cautionNotes?.map((note, i) => (
          <p key={i} className="text-xs text-amber-600 dark:text-amber-400">
            ⚠ {note}
          </p>
        ))}

      {/* APPROVE dialog */}
      <AlertDialog open={approveOpen} onOpenChange={setApproveOpen}>
        <AlertDialogContent onEscapeKeyDown={(e) => e.preventDefault()}>
          <AlertDialogHeader>
            <AlertDialogTitle>Confirm Trade</AlertDialogTitle>
            <AlertDialogDescription asChild>
              <div className="space-y-1 text-sm">
                <p>
                  {signal.signal} {signal.pair} @ {signal.parameters?.entry.toFixed(4) ?? '—'}
                </p>
                <p>
                  Stop Loss: {signal.parameters?.stopLoss.toFixed(4) ?? '—'} | Take Profit:{' '}
                  {signal.parameters?.takeProfit.toFixed(4) ?? '—'}
                </p>
                <p className={cn(
                  'font-semibold',
                  mode === 'MIFX_DEMO'
                    ? 'text-emerald-600 dark:text-emerald-400'
                    : 'text-amber-600 dark:text-amber-400',
                )}>
                  Risk: ${signal.parameters?.riskAmount.toFixed(2) ?? '—'} (
                  {signal.parameters?.riskPercent.toFixed(2) ?? '—'}% equity) —{' '}
                  {mode === 'MIFX_DEMO' ? '🟢 LIVE ke MIFX' : '🔵 SIMULATION'}
                </p>
              </div>
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className="bg-emerald-600 hover:bg-emerald-700"
              onClick={onApprove}
            >
              Confirm Trade ▶
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* REJECT dialog */}
      <AlertDialog open={rejectOpen} onOpenChange={setRejectOpen}>
        <AlertDialogContent onEscapeKeyDown={(e) => e.preventDefault()}>
          <AlertDialogHeader>
            <AlertDialogTitle>Reject Signal</AlertDialogTitle>
            <AlertDialogDescription>
              This signal will be dismissed. The pipeline will await the next analysis trigger.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Keep Signal</AlertDialogCancel>
            <AlertDialogAction
              className="border border-red-300 dark:border-red-700 text-red-600 dark:text-red-400 bg-transparent hover:bg-red-50 dark:hover:bg-red-950/30"
              onClick={onReject}
            >
              Reject
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
