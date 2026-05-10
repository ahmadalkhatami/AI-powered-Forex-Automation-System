# Story 2.2: Layout Shell (ModeBanner + Header)

Status: ready-for-dev

## Story

As a trader,
I want the dashboard to immediately show SIMULATION vs LIVE mode and display the currency pair + timeframe in the header,
so that I never mistake the mode I'm operating in.

## Acceptance Criteria

1. `ModeBanner` component at `src/components/layout/ModeBanner.tsx`:
   - `simulation` prop: `bg-amber-400 text-amber-950` full-width, "⚠ SIMULATION MODE — No real trades are executed"
   - `live` prop: `bg-red-700 text-white` full-width, "🔴 LIVE MODE — Trades execute with real capital"
   - `role="alert"` on the banner element — screen readers announce immediately on render
   - Not dismissable (no close button)
   - Always full-width, sticky to top (above header)
2. `Header` component at `src/components/layout/Header.tsx`:
   - Displays trading pair + timeframe (e.g., "EUR/USD · M15") as primary title
   - Ghost link to `/history` page labeled "History ↗"
   - Dark mode toggle icon button (sun/moon icon)
3. Root layout `src/app/layout.tsx`:
   - ModeBanner rendered above Header above `{children}`
   - `ThemeProvider` wraps entire layout for dark mode support (next-themes)
4. Main page `src/app/page.tsx`:
   - 2-column grid on `xl` breakpoint: `grid-cols-[65%_35%]`
   - Single column below `md`: `md:grid-cols-1`
   - Main column (left) and sidebar column (right)
5. `npm run build` passes with no TypeScript errors

## Tasks / Subtasks

- [ ] Install `next-themes` for dark mode (AC: 3)
  - [ ] `npm install next-themes`
- [ ] Create `ThemeProvider` client wrapper (AC: 3)
  - [ ] `src/components/layout/ThemeProvider.tsx` — wraps `next-themes` Provider
- [ ] Create `ModeBanner` (AC: 1)
  - [ ] Props: `mode: 'simulation' | 'live'`
  - [ ] Two variant styles
  - [ ] `role="alert"`
- [ ] Create `Header` (AC: 2)
  - [ ] Pair + timeframe display (hardcoded "EUR/USD · M15" for now)
  - [ ] History link using Next.js `<Link>`
  - [ ] Dark mode toggle using `useTheme()` from next-themes
- [ ] Update root layout (AC: 3)
  - [ ] Add ThemeProvider, ModeBanner, Header
  - [ ] Add `suppressHydrationWarning` on `<html>` (required by next-themes)
- [ ] Create page layout grid (AC: 4)
  - [ ] `src/app/page.tsx` with 2-column responsive grid

## Dev Notes

### next-themes Setup

```tsx
// src/components/layout/ThemeProvider.tsx
'use client'
import { ThemeProvider as NextThemesProvider } from 'next-themes'

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  return (
    <NextThemesProvider attribute="class" defaultTheme="system" enableSystem>
      {children}
    </NextThemesProvider>
  )
}
```

Add `attribute="class"` so dark mode works with Tailwind's `dark:` variant.

### Dark Mode Toggle in Header

```tsx
'use client'
import { useTheme } from 'next-themes'
import { Sun, Moon } from 'lucide-react'  // lucide-react is pre-installed by shadcn/ui

export function Header() {
  const { theme, setTheme } = useTheme()
  return (
    <header className="...">
      <span className="font-semibold">EUR/USD · M15</span>
      <div className="flex items-center gap-2">
        <Link href="/history" className="...">History ↗</Link>
        <button
          onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
          className="p-2 rounded-md hover:bg-muted"
          aria-label="Toggle dark mode"
        >
          {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
        </button>
      </div>
    </header>
  )
}
```

### Root Layout Structure

```tsx
// src/app/layout.tsx
export default function RootLayout({ children }) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body className={`${inter.variable} ${jetbrainsMono.variable} font-sans`}>
        <ThemeProvider>
          <ModeBanner mode="simulation" />
          <Header />
          <main className="max-w-7xl mx-auto px-8 py-6">
            {children}
          </main>
        </ThemeProvider>
      </body>
    </html>
  )
}
```

### Page Layout Grid

```tsx
// src/app/page.tsx
export default function DashboardPage() {
  return (
    <div className="grid grid-cols-1 xl:grid-cols-[65%_35%] gap-4">
      <div className="space-y-4">
        {/* Main column: signal flow */}
        <p className="text-muted-foreground">Signal area — coming in stories 2.3+</p>
      </div>
      <div className="space-y-4">
        {/* Sidebar: account + positions */}
        <p className="text-muted-foreground">Sidebar — coming in story 4.1</p>
      </div>
    </div>
  )
}
```

### ModeBanner Typography

Per UX spec:
- SIMULATION: `bg-amber-400` — amber banner is *always* amber-400, both light and dark modes
- LIVE: `bg-red-700 text-white` — full-width, high contrast warning

Text should be `text-sm font-medium text-center py-2`.

### References

- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Visual Design Foundation]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#UX Consistency Patterns - Navigation Patterns]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Component Strategy - ModeBanner]
- UX spec: ModeBanner `role="alert"`, never dismissable, always full-width sticky top

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

### File List
