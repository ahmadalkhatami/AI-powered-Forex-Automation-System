'use client'

interface PatternIconProps {
  name: string
  bias: 'Bullish' | 'Bearish' | 'Neutral'
  size?: number
}

/**
 * Mini SVG icon untuk candlestick pattern.
 * Color follow bias: Bullish=emerald, Bearish=red, Neutral=amber.
 */
export function PatternIcon({ name, bias, size = 16 }: PatternIconProps) {
  const color =
    bias === 'Bullish' ? '#10b981' :
    bias === 'Bearish' ? '#ef4444' :
    '#f59e0b'

  const stroke = color
  const fill = color + '40'

  if (name.includes('Pin Bar')) {
    // Long wick + small body. Bullish: lower wick, body atas. Bearish: upper wick, body bawah.
    const isBullish = bias === 'Bullish'
    return (
      <svg width={size} height={size} viewBox="0 0 16 16">
        <line x1="8" y1="1" x2="8" y2="15" stroke={stroke} strokeWidth="1" />
        {isBullish ? (
          <rect x="6" y="2" width="4" height="4" fill={fill} stroke={stroke} />
        ) : (
          <rect x="6" y="10" width="4" height="4" fill={fill} stroke={stroke} />
        )}
      </svg>
    )
  }

  if (name.includes('Engulfing')) {
    // 2 candle, second bigger
    return (
      <svg width={size} height={size} viewBox="0 0 16 16">
        <rect x="2" y="6" width="3" height="4" fill="none" stroke={stroke} strokeWidth="0.8" />
        <rect x="8" y="3" width="5" height="10" fill={fill} stroke={stroke} />
      </svg>
    )
  }

  if (name === 'Doji') {
    // Cross shape — small body, long wick top+bottom
    return (
      <svg width={size} height={size} viewBox="0 0 16 16">
        <line x1="8" y1="1" x2="8" y2="15" stroke={stroke} strokeWidth="1" />
        <line x1="4" y1="8" x2="12" y2="8" stroke={stroke} strokeWidth="2" />
      </svg>
    )
  }

  if (name.includes('Marubozu')) {
    // Full body, no wicks
    return (
      <svg width={size} height={size} viewBox="0 0 16 16">
        <rect x="5" y="2" width="6" height="12" fill={fill} stroke={stroke} />
      </svg>
    )
  }

  if (name === 'Inside Bar') {
    // 2 nested rectangles
    return (
      <svg width={size} height={size} viewBox="0 0 16 16">
        <rect x="2" y="3" width="5" height="10" fill="none" stroke={stroke} strokeWidth="0.8" />
        <rect x="9" y="5" width="4" height="6" fill={fill} stroke={stroke} />
      </svg>
    )
  }

  if (name.includes('Star')) {
    // 3 candles with small middle (star)
    const isBullish = bias === 'Bullish'
    return (
      <svg width={size} height={size} viewBox="0 0 16 16">
        {isBullish ? (
          <>
            <rect x="1" y="3" width="3" height="9" fill="#ef444440" stroke="#ef4444" strokeWidth="0.7" />
            <rect x="6" y="7" width="3" height="2" fill="#f59e0b40" stroke="#f59e0b" strokeWidth="0.7" />
            <rect x="11" y="3" width="3" height="9" fill={fill} stroke={stroke} />
          </>
        ) : (
          <>
            <rect x="1" y="3" width="3" height="9" fill="#10b98140" stroke="#10b981" strokeWidth="0.7" />
            <rect x="6" y="7" width="3" height="2" fill="#f59e0b40" stroke="#f59e0b" strokeWidth="0.7" />
            <rect x="11" y="3" width="3" height="9" fill={fill} stroke={stroke} />
          </>
        )}
      </svg>
    )
  }

  // Unknown / None — fallback dot
  return (
    <svg width={size} height={size} viewBox="0 0 16 16">
      <circle cx="8" cy="8" r="2" fill={fill} stroke={stroke} strokeWidth="0.8" />
    </svg>
  )
}
