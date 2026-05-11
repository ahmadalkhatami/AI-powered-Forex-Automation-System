interface ModeBannerProps {
  mode: 'simulation' | 'live'
}

export function ModeBanner({ mode }: ModeBannerProps) {
  const isSimulation = mode === 'simulation'

  return (
    <div
      role="alert"
      className={`w-full sticky top-0 z-50 text-sm font-medium text-center py-2 ${
        isSimulation
          ? 'bg-amber-400 text-amber-950'
          : 'bg-red-700 text-white'
      }`}
    >
      {isSimulation
        ? '⚠ SIMULATION MODE — No real trades are executed'
        : '🔴 LIVE MODE — Trades execute with real capital'}
    </div>
  )
}
