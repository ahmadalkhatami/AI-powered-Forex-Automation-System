'use client'

import { MousePointer2, Minus, TrendingUp, Square, Trash2, MoveUpRight, Type, Ruler } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { DrawingType } from '@/lib/drawings'

export type ToolMode = 'cursor' | DrawingType

interface ChartToolbarProps {
  activeTool: ToolMode
  onSelectTool: (tool: ToolMode) => void
  onClearAll?: () => void
  onDeleteSelected?: () => void
  selectedId: string | null
  drawingCount: number
}

const TOOLS: Array<{ mode: ToolMode; icon: typeof MousePointer2; label: string }> = [
  { mode: 'cursor',    icon: MousePointer2, label: 'Cursor — pan/zoom + klik drawing untuk select' },
  { mode: 'hline',     icon: Minus,         label: 'Horizontal line — klik level' },
  { mode: 'trendline', icon: TrendingUp,    label: 'Trend line — klik 2 titik' },
  { mode: 'ray',       icon: MoveUpRight,   label: 'Ray — klik 2 titik, extend infinity' },
  { mode: 'rectangle', icon: Square,        label: 'Rectangle — klik diagonal' },
  { mode: 'text',      icon: Type,          label: 'Text annotation — klik posisi, masukkan text' },
  { mode: 'measure',   icon: Ruler,         label: 'Measure — klik 2 titik, lihat pip/% /waktu' },
]

export function ChartToolbar({
  activeTool,
  onSelectTool,
  onClearAll,
  onDeleteSelected,
  selectedId,
  drawingCount,
}: ChartToolbarProps) {
  return (
    <div className="flex items-center gap-1">
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
      {selectedId && onDeleteSelected && (
        <button
          onClick={onDeleteSelected}
          title="Hapus drawing terpilih (DEL)"
          className="p-1.5 rounded border border-red-500/40 bg-red-500/10 text-red-500 hover:bg-red-500/20 transition-colors ml-1"
        >
          <Trash2 className="h-3.5 w-3.5" />
        </button>
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
