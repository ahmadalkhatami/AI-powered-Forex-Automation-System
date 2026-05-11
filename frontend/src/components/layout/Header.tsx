'use client'

import { useState, useEffect } from 'react'
import Link from 'next/link'
import { useTheme } from 'next-themes'
import { Sun, Moon } from 'lucide-react'

export function Header() {
  const { theme, setTheme } = useTheme()
  const [mounted, setMounted] = useState(false)

  useEffect(() => {
    setMounted(true)
  }, [])

  return (
    <header className="w-full border-b border-border bg-background px-8 py-3 flex items-center justify-between">
      <span className="font-semibold text-foreground">EUR/USD · M15</span>
      <div className="flex items-center gap-2">
        <Link
          href="/history"
          className="text-sm text-muted-foreground hover:text-foreground transition-colors"
        >
          History ↗
        </Link>
        <button
          onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
          className="p-2 rounded-md hover:bg-muted transition-colors"
          aria-label="Toggle dark mode"
        >
          {mounted ? (theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />) : <Moon size={18} />}
        </button>
      </div>
    </header>
  )
}
