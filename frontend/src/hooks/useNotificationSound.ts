'use client'

import { useCallback, useEffect, useState } from 'react'

const STORAGE_KEY = 'forexai.soundEnabled'

/**
 * Web Audio API beeps untuk trading event notification. No external assets —
 * generate via oscillator. Persist enable/disable di localStorage.
 *
 * <para>3 beep variants:</para>
 * <list type="bullet">
 *   <item><b>Signal</b>: short rising tone (alert attention)</item>
 *   <item><b>Win</b>: 2-note ascending major (positive)</item>
 *   <item><b>Loss</b>: descending minor (negative)</item>
 * </list>
 */
export function useNotificationSound() {
  const [enabled, setEnabledState] = useState(true)

  useEffect(() => {
    if (typeof window === 'undefined') return
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved !== null) setEnabledState(saved === 'true')
  }, [])

  const setEnabled = useCallback((next: boolean) => {
    setEnabledState(next)
    if (typeof window !== 'undefined') localStorage.setItem(STORAGE_KEY, String(next))
  }, [])

  const playBeep = useCallback((freq: number, duration: number, type: OscillatorType = 'sine') => {
    if (!enabled || typeof window === 'undefined') return
    try {
      // @ts-expect-error webkitAudioContext for Safari fallback
      const AudioCtx = window.AudioContext || window.webkitAudioContext
      if (!AudioCtx) return
      const ctx = new AudioCtx()
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = type
      osc.frequency.value = freq
      gain.gain.value = 0.15  // moderate volume
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start()
      // Fade out untuk smoothness (avoid click)
      gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + duration)
      osc.stop(ctx.currentTime + duration)
    } catch {
      // Silent — audio not supported
    }
  }, [enabled])

  const playSignal = useCallback(() => {
    playBeep(660, 0.15)
    setTimeout(() => playBeep(880, 0.20), 120)
  }, [playBeep])

  const playWin = useCallback(() => {
    playBeep(523, 0.15)        // C5
    setTimeout(() => playBeep(659, 0.15), 130)   // E5
    setTimeout(() => playBeep(784, 0.25), 260)   // G5
  }, [playBeep])

  const playLoss = useCallback(() => {
    playBeep(440, 0.20, 'square')
    setTimeout(() => playBeep(330, 0.30, 'square'), 180)
  }, [playBeep])

  return { enabled, setEnabled, playSignal, playWin, playLoss }
}
