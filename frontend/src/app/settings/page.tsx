'use client'

import { useEffect, useState } from 'react'
import { Save, RotateCcw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useToast } from '@/hooks/use-toast'
import { getSettings, updateSettings } from '@/lib/api'
import type { SettingsResponse } from '@/lib/types'

interface FieldDef {
  key: keyof SettingsResponse
  label: string
  description: string
  unit: string
  step: number
  min: number
  max: number
  type: 'number' | 'percent'
}

const FIELDS: FieldDef[] = [
  { key: 'maxSpreadPips',        label: 'Max Spread',           description: 'Reject trade kalau broker spread > nilai ini (pips)', unit: 'pips', step: 0.1, min: 0.5, max: 10,   type: 'number' },
  { key: 'maxConsecutiveLosses', label: 'Max Consecutive Loss', description: 'HALT setelah N loss berturut-turut', unit: 'losses', step: 1, min: 2, max: 10, type: 'number' },
  { key: 'maxHoldingMinutes',    label: 'Time Stop (legacy fallback)', description: 'Per-TF time stop pakai 6h/24h/7d. Ini fallback untuk legacy trade tanpa TF metadata.', unit: 'minutes', step: 30, min: 30, max: 10080, type: 'number' },
  { key: 'cooldownMinutes',      label: 'Post-Loss Cooldown',   description: 'Block same-direction signal selama N menit setelah LOSS', unit: 'minutes', step: 5, min: 0, max: 120, type: 'number' },
  { key: 'nanoMaxDailyLossUsd',  label: 'Nano Max Daily Loss',  description: 'Auto-halt kalau realized loss hari ini melewati nilai ini ($)', unit: '$', step: 1, min: 1, max: 50, type: 'number' },
  { key: 'nanoEquityFloorUsd',   label: 'Nano Equity Floor',    description: 'Permanent halt kalau equity drop ke level ini ($)', unit: '$', step: 1, min: 5, max: 100, type: 'number' },
  { key: 'maxWeeklyDrawdownPct', label: 'Weekly DD Cap',        description: 'Halt kalau realized loss 7 hari > N% equity', unit: '%', step: 0.005, min: 0.01, max: 0.20, type: 'percent' },
  { key: 'maxTradesPerDay',      label: 'Max Trades/Day',       description: 'Block execute kalau hari ini sudah open N trade — overtrade prevention untuk M15 scalping', unit: 'trades', step: 1, min: 1, max: 50, type: 'number' },
  { key: 'autoApproveMinConfidence', label: 'Auto-Approve Min Confidence', description: 'Confidence minimum untuk auto-execute (Exec ON mode). Counter-D1 setup require +5% extra. Manual approve always works regardless.', unit: '%', step: 0.01, min: 0.30, max: 0.95, type: 'percent' },
]

export default function SettingsPage() {
  const { toast } = useToast()
  const [config, setConfig] = useState<SettingsResponse | null>(null)
  const [draft, setDraft] = useState<Partial<SettingsResponse>>({})
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    getSettings()
      .then((c) => { setConfig(c); setDraft(c) })
      .catch(() => toast({ title: 'Gagal load settings', variant: 'destructive' }))
  }, [toast])

  const handleChange = (key: keyof SettingsResponse, value: number) => {
    setDraft((d) => ({ ...d, [key]: value }))
  }

  const handleReset = () => {
    if (config) setDraft(config)
  }

  const handleSave = async () => {
    if (!config) return
    setSaving(true)
    try {
      // Hanya kirim field yang berubah
      const diff: Record<string, number> = {}
      for (const f of FIELDS) {
        if (draft[f.key] !== config[f.key]) {
          diff[f.key] = draft[f.key] as number
        }
      }
      if (Object.keys(diff).length === 0) {
        toast({ title: 'Tidak ada perubahan', description: 'Settings sama dengan current config' })
        setSaving(false)
        return
      }
      const updated = await updateSettings(diff)
      setConfig(updated)
      setDraft(updated)
      toast({ title: 'Settings tersimpan', description: `${Object.keys(diff).length} field diupdate` })
    } catch (err) {
      toast({
        title: 'Gagal save',
        description: err instanceof Error ? err.message : 'Unknown error',
        variant: 'destructive',
      })
    } finally {
      setSaving(false)
    }
  }

  if (!config) {
    return (
      <div className="p-8 text-muted-foreground">Loading settings…</div>
    )
  }

  const hasChanges = FIELDS.some((f) => draft[f.key] !== config[f.key])

  return (
    <div className="p-4 sm:p-8 max-w-3xl mx-auto space-y-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-bold">Safety Thresholds</h1>
        <p className="text-sm text-muted-foreground">
          Risk management config. Persist ke <code className="bg-muted px-1 rounded">data/{config.isHalted ? 'real' : 'demo'}/system-state.json</code> setelah save.
        </p>
      </div>

      {config.isHalted && (
        <div className="rounded-lg border border-red-500/40 bg-red-500/10 p-3">
          <p className="text-sm font-semibold text-red-500">⛔ System HALTED</p>
          {config.haltReason && (
            <p className="text-xs text-muted-foreground mt-1">{config.haltReason}</p>
          )}
        </div>
      )}

      <div className="space-y-4">
        {FIELDS.map((f) => {
          const currentValue = draft[f.key] as number
          const orig = config[f.key] as number
          const changed = currentValue !== orig
          const displayUnit = f.type === 'percent' ? '%' : f.unit

          return (
            <div
              key={f.key}
              className={`rounded-lg border p-4 space-y-2 transition-colors ${
                changed ? 'border-amber-500/50 bg-amber-500/5' : 'border-border'
              }`}
            >
              <div className="flex items-baseline justify-between gap-3">
                <label className="text-sm font-semibold">{f.label}</label>
                <span className="text-xs text-muted-foreground font-mono">
                  current: {f.type === 'percent' ? (orig * 100).toFixed(1) : orig}{displayUnit}
                </span>
              </div>
              <p className="text-xs text-muted-foreground">{f.description}</p>
              <div className="flex items-center gap-2">
                <input
                  type="number"
                  className="flex-1 rounded-md border border-input bg-background px-3 py-1.5 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-ring"
                  value={f.type === 'percent' ? (currentValue * 100).toFixed(1) : currentValue}
                  step={f.type === 'percent' ? 0.1 : f.step}
                  min={f.type === 'percent' ? f.min * 100 : f.min}
                  max={f.type === 'percent' ? f.max * 100 : f.max}
                  onChange={(e) => {
                    const v = parseFloat(e.target.value)
                    if (Number.isFinite(v)) {
                      handleChange(f.key, f.type === 'percent' ? v / 100 : v)
                    }
                  }}
                />
                <span className="text-xs text-muted-foreground w-12">{displayUnit}</span>
              </div>
            </div>
          )
        })}
      </div>

      <div className="flex items-center justify-end gap-2 sticky bottom-4 bg-background/95 backdrop-blur p-3 rounded-lg border border-border">
        <Button
          variant="ghost"
          size="sm"
          onClick={handleReset}
          disabled={!hasChanges || saving}
          className="gap-2"
        >
          <RotateCcw size={14} />
          Reset
        </Button>
        <Button
          size="sm"
          onClick={handleSave}
          disabled={!hasChanges || saving}
          className="gap-2"
        >
          <Save size={14} />
          {saving ? 'Saving…' : `Save ${hasChanges ? '(unsaved changes)' : ''}`}
        </Button>
      </div>
    </div>
  )
}
