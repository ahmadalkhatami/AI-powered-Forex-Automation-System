'use client'

import { useState } from 'react'
import {
  MousePointer2, Minus, TrendingUp, Square, Trash2, MoveUpRight, Type, Ruler,
  GitFork, GitBranch, Magnet, Lock, LockOpen, Palette, EyeOff,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { COLOR_PRESETS, THICKNESS_PRESETS, type DrawingType, type DrawingStyle } from '@/lib/drawings'

export type ToolMode = 'cursor' | DrawingType

interface ChartToolbarProps {
  activeTool: ToolMode
  onSelectTool: (tool: ToolMode) => void
  onClearAll?: () => void
  onDeleteSelected?: () => void
  selectedId: string | null
  selectedStyle?: DrawingStyle
  selectedLocked?: boolean
  onUpdateSelectedStyle?: (style: Partial<DrawingStyle>) => void
  onToggleLockSelected?: () => void
  drawingCount: number
  snapEnabled?: boolean
  onToggleSnap?: () => void
  /** Jumlah position box yang sudah di-dismiss user (untuk restore button) */
  dismissedBoxCount?: number
  onRestoreDismissedBoxes?: () => void
}

const TOOLS: Array<{ mode: ToolMode; icon: typeof MousePointer2; label: string }> = [
  { mode: 'cursor',          icon: MousePointer2, label: 'Cursor — pan/zoom + klik drawing untuk select' },
  { mode: 'hline',           icon: Minus,         label: 'Horizontal line — klik level' },
  { mode: 'trendline',       icon: TrendingUp,    label: 'Trend line — klik 2 titik' },
  { mode: 'ray',             icon: MoveUpRight,   label: 'Ray — klik 2 titik, extend infinity' },
  { mode: 'rectangle',       icon: Square,        label: 'Rectangle — klik diagonal' },
  { mode: 'text',            icon: Type,          label: 'Text annotation — klik posisi, masukkan text' },
  { mode: 'measure',         icon: Ruler,         label: 'Measure — klik 2 titik, lihat pip/% /waktu' },
  { mode: 'fib-retracement', icon: GitFork,       label: 'Fib retracement — klik 2 titik (high → low)' },
  { mode: 'fib-extension',   icon: GitBranch,     label: 'Fib extension — klik 3 titik (A → B → C)' },
]

export function ChartToolbar({
  activeTool,
  onSelectTool,
  onClearAll,
  onDeleteSelected,
  selectedId,
  selectedStyle,
  selectedLocked,
  onUpdateSelectedStyle,
  onToggleLockSelected,
  drawingCount,
  snapEnabled,
  onToggleSnap,
  dismissedBoxCount,
  onRestoreDismissedBoxes,
}: ChartToolbarProps) {
  const [paletteOpen, setPaletteOpen] = useState(false)
  return (
    <div className="flex items-center gap-1 relative">
      {TOOLS.map(({ mode, icon: Icon, label }) => (
        <button
          key={mode}
          onClick={() => onSelectTool(mode)}
          title={label}
          className={cn(
            'p-1.5 rounded border transition-colors',
            activeTool === mode
              ? 'bg-primary/15 border-primary/40 text-primary'
              : 'border-border/40 text-muted-foreground hover:text-foreground hover:border-border',
          )}
        >
          <Icon className="h-3.5 w-3.5" />
        </button>
      ))}
      {onRestoreDismissedBoxes && dismissedBoxCount !== undefined && dismissedBoxCount > 0 && (
        <button
          onClick={onRestoreDismissedBoxes}
          title={`Tampilkan kembali ${dismissedBoxCount} position box yang di-hide`}
          className="p-1.5 rounded border border-blue-500/40 bg-blue-500/10 text-blue-500 hover:bg-blue-500/20 transition-colors ml-1 flex items-center gap-1"
        >
          <EyeOff className="h-3.5 w-3.5" />
          <span className="text-[10px] font-mono">{dismissedBoxCount}</span>
        </button>
      )}
      {onToggleSnap && (
        <button
          onClick={onToggleSnap}
          title={snapEnabled
            ? 'Magnet ON — klik akan snap ke high/low candle terdekat'
            : 'Magnet OFF — klik free di posisi mouse'}
          className={cn(
            'p-1.5 rounded border transition-colors ml-1',
            snapEnabled
              ? 'bg-amber-500/15 border-amber-500/40 text-amber-500'
              : 'border-border/40 text-muted-foreground hover:text-foreground hover:border-border',
          )}
        >
          <Magnet className="h-3.5 w-3.5" />
        </button>
      )}
      {selectedId && onUpdateSelectedStyle && selectedStyle && (
        <button
          onClick={() => setPaletteOpen((v) => !v)}
          title="Ubah warna & tebal garis drawing terpilih"
          className={cn(
            'p-1.5 rounded border transition-colors ml-1',
            paletteOpen
              ? 'bg-primary/15 border-primary/40 text-primary'
              : 'border-border/40 text-muted-foreground hover:text-foreground hover:border-border',
          )}
        >
          <Palette className="h-3.5 w-3.5" />
        </button>
      )}
      {selectedId && onToggleLockSelected && (
        <button
          onClick={onToggleLockSelected}
          title={selectedLocked ? 'Unlock drawing — bisa di-edit/delete lagi' : 'Lock drawing — protect dari edit/delete'}
          className={cn(
            'p-1.5 rounded border transition-colors',
            selectedLocked
              ? 'bg-amber-500/15 border-amber-500/40 text-amber-500'
              : 'border-border/40 text-muted-foreground hover:text-foreground hover:border-border',
          )}
        >
          {selectedLocked ? <Lock className="h-3.5 w-3.5" /> : <LockOpen className="h-3.5 w-3.5" />}
        </button>
      )}
      {selectedId && !selectedLocked && onDeleteSelected && (
        <button
          onClick={onDeleteSelected}
          title="Hapus drawing terpilih (DEL)"
          className="p-1.5 rounded border border-red-500/40 bg-red-500/10 text-red-500 hover:bg-red-500/20 transition-colors"
        >
          <Trash2 className="h-3.5 w-3.5" />
        </button>
      )}
      {/* Style picker popover */}
      {paletteOpen && selectedId && selectedStyle && onUpdateSelectedStyle && (
        <div className="absolute top-full right-0 mt-1 z-10 bg-background border border-border/60 rounded-md p-2 shadow-lg space-y-2 min-w-[200px]">
          <div>
            <div className="text-[10px] uppercase tracking-wider text-muted-foreground mb-1">Color</div>
            <div className="grid grid-cols-5 gap-1">
              {COLOR_PRESETS.map((c) => (
                <button
                  key={c}
                  onClick={() => onUpdateSelectedStyle({ color: c })}
                  className={cn(
                    'h-5 w-5 rounded border-2 transition-transform hover:scale-110',
                    selectedStyle.color === c ? 'border-foreground scale-110' : 'border-transparent',
                  )}
                  style={{ backgroundColor: c }}
                  title={c}
                />
              ))}
            </div>
          </div>
          <div>
            <div className="text-[10px] uppercase tracking-wider text-muted-foreground mb-1">Thickness</div>
            <div className="flex gap-1">
              {THICKNESS_PRESETS.map((w) => (
                <button
                  key={w}
                  onClick={() => onUpdateSelectedStyle({ width: w })}
                  className={cn(
                    'flex-1 h-7 rounded border flex items-center justify-center transition-colors',
                    selectedStyle.width === w
                      ? 'bg-primary/15 border-primary/40 text-primary'
                      : 'border-border/40 text-muted-foreground hover:text-foreground hover:border-border',
                  )}
                  title={`${w}px`}
                >
                  <span
                    className="rounded-sm bg-current"
                    style={{ width: '16px', height: `${w}px` }}
                  />
                </button>
              ))}
            </div>
          </div>
        </div>
      )}
      {drawingCount > 0 && onClearAll && (
        <>
          <span className="text-xs text-muted-foreground font-mono ml-1">{drawingCount}</span>
          {!selectedId && (
            <button
              onClick={onClearAll}
              title="Hapus semua drawing di pair+TF ini"
              className="p-1.5 rounded border border-border/40 text-muted-foreground hover:text-red-500 hover:border-red-500/40 transition-colors"
            >
              <Trash2 className="h-3.5 w-3.5" />
            </button>
          )}
        </>
      )}
    </div>
  )
}
