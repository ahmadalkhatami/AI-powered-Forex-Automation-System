# Story 2.1: Next.js 14 Project Setup

Status: ready-for-dev

## Story

As a frontend developer,
I want a properly configured Next.js 14 App Router project with Tailwind CSS, shadcn/ui, and Tremor installed,
so that I can build components using the agreed design system without configuration friction.

## Acceptance Criteria

1. `frontend/` directory at project root with Next.js 14 App Router project
2. Tailwind CSS 3.4+ with custom color tokens in `tailwind.config.ts`:
   - `signal-buy`: `{ DEFAULT: colors.emerald[600], dark: colors.emerald[400] }`
   - `signal-sell`: `{ DEFAULT: colors.red[600], dark: colors.red[400] }`
   - `signal-hold`: `{ DEFAULT: colors.slate[500], dark: colors.slate[400] }`
   - `decision-go`: `colors.emerald[600]`
   - `decision-nogo`: `colors.red[600]`
   - `simulation-banner`: `colors.amber[400]`
   - `live-banner`: `colors.red[700]`
3. shadcn/ui initialized with `slate` base color, components in `src/components/ui/`
4. These shadcn/ui components pre-installed: `button`, `card`, `badge`, `collapsible`, `dialog`, `alert-dialog`, `separator`, `toast`
5. Tremor 3.x installed as `@tremor/react`
6. Fonts configured via `next/font/google`: Inter for UI, JetBrains Mono for numbers, applied to root layout
7. Path alias `@/` → `src/` in `tsconfig.json`
8. `npm run dev` starts on `http://localhost:3000` with no errors
9. `npm run build` passes TypeScript strict mode with zero errors

## Tasks / Subtasks

- [ ] Scaffold Next.js 14 project (AC: 1)
  - [ ] `npx create-next-app@latest frontend --typescript --tailwind --eslint --app --src-dir --import-alias "@/*"`
- [ ] Configure custom Tailwind color tokens (AC: 2)
  - [ ] Extend theme in `tailwind.config.ts`
- [ ] Initialize shadcn/ui (AC: 3)
  - [ ] `cd frontend && npx shadcn-ui@latest init` — choose: TypeScript, slate, CSS variables yes
- [ ] Install shadcn/ui components (AC: 4)
  - [ ] `npx shadcn-ui@latest add button card badge collapsible dialog alert-dialog separator toast`
- [ ] Install Tremor (AC: 5)
  - [ ] `npm install @tremor/react`
- [ ] Configure fonts in root layout (AC: 6)
  - [ ] Import Inter + JetBrains Mono in `src/app/layout.tsx`
  - [ ] Apply as CSS variables: `--font-sans`, `--font-mono`
  - [ ] Update `tailwind.config.ts` fontFamily to use CSS vars
- [ ] Verify path alias works (AC: 7)
- [ ] Verify `npm run dev` and `npm run build` (AC: 8, 9)

## Dev Notes

### `tailwind.config.ts` Color Extension

```typescript
import colors from 'tailwindcss/colors'

const config: Config = {
  content: ['./src/**/*.{ts,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        'signal-buy': { DEFAULT: colors.emerald[600], dark: colors.emerald[400] },
        'signal-sell': { DEFAULT: colors.red[600], dark: colors.red[400] },
        'signal-hold': { DEFAULT: colors.slate[500], dark: colors.slate[400] },
        'decision-go': colors.emerald[600],
        'decision-nogo': colors.red[600],
        'simulation-banner': colors.amber[400],
        'live-banner': colors.red[700],
      },
      fontFamily: {
        sans: ['var(--font-sans)', ...fontFamily.sans],
        mono: ['var(--font-mono)', ...fontFamily.mono],
      },
    },
  },
}
```

### Root Layout Font Setup

```tsx
// src/app/layout.tsx
import { Inter, JetBrains_Mono } from 'next/font/google'

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-sans',
})

const jetbrainsMono = JetBrains_Mono({
  subsets: ['latin'],
  variable: '--font-mono',
})

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body className={`${inter.variable} ${jetbrainsMono.variable} font-sans`}>
        {children}
      </body>
    </html>
  )
}
```

### shadcn/ui Init Config (when prompted)

```
Would you like to use TypeScript? yes
Which style? Default
Which base color? Slate
Where is your global CSS file? src/app/globals.css
Do you want to use CSS variables? yes
Where is your tailwind.config? tailwind.config.ts
Configure the import alias? @/components
```

### Tremor + Tailwind Compatibility

Tremor 3.x requires adding Tremor's content path to Tailwind config:
```typescript
content: [
  './src/**/*.{ts,tsx}',
  './node_modules/@tremor/**/*.{js,ts,jsx,tsx}',  // Required for Tremor
],
```

### Project Structure After Setup

```
frontend/
├── src/
│   ├── app/
│   │   ├── layout.tsx          # Root layout with fonts + providers
│   │   ├── page.tsx            # Main dashboard (placeholder for now)
│   │   └── globals.css         # shadcn/ui CSS variables
│   ├── components/
│   │   └── ui/                 # shadcn/ui primitives (auto-generated)
│   └── lib/
│       └── utils.ts            # shadcn/ui cn() utility
├── tailwind.config.ts
├── tsconfig.json               # @/ alias configured
├── next.config.mjs
└── package.json
```

### References

- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Design System Foundation]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md#Visual Design Foundation]
- Stack: React 18 + Next.js 14 (App Router) + Tailwind CSS 3.4+ + shadcn/ui + Tremor 3.x

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

### File List
