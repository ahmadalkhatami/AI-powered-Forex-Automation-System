export interface AccountHealthData {
  equity: number
  peakEquity: number
  drawdownPct: number     // 0.0–1.0
  openPositions: number
  maxPositions: number    // always 3
  totalTrades?: number
  winRate?: number        // 0.0–1.0
  source?: 'LIVE' | 'SIMULATION'   // LIVE = dari akun broker nyata

  // Tier-based risk + daily cap (Sprint 1 item 1)
  riskTier?: 'starter' | 'growth' | 'stable' | 'scaled'
  riskPerTradePct?: number       // 0.020 = 2%
  dailyCapPct?: number           // 0.060 = 6%
  maxDailyTrades?: number        // 3 / 4 / 5
  dailyRiskUsedUsd?: number      // total $ risk dipakai hari ini
  tradesOpenedToday?: number     // jumlah trade dibuka hari ini
  dailyCapUtilization?: number   // 0.0–1.0+ — >1 = cap terlewat
}

export interface CandleBar {
  time: number            // Unix timestamp seconds
  open: number
  high: number
  low: number
  close: number
}

export interface MifxStatusResponse {
  connected: boolean
  pair: string | null
  bid: number | null
  ask: number | null
  mid: number | null
  spreadPips: number | null
  time: string | null
}

export interface AccountHealthResponse {
  equity: number
  peakEquity: number
  realizedEquity: number
  unrealizedPnl: number
  drawdownPct: number
  openPositions: number
  maxPositions: number
  totalTrades: number
  winRate: number
  source: 'LIVE' | 'SIMULATION'

  // Tier-based risk + daily cap
  riskTier: 'starter' | 'growth' | 'stable' | 'scaled'
  riskPerTradePct: number
  dailyCapPct: number
  maxDailyTrades: number
  dailyRiskUsedUsd: number
  tradesOpenedToday: number
  dailyCapUtilization: number
}

export interface PositionCardData {
  tradeId: string
  pair: string
  direction: 'BUY' | 'SELL'
  entry: number
  currentPrice?: number
  floatingPnl: number       // in USD
  floatingPnlPips: number
  distanceToSlPips?: number
  distanceToTpPips?: number
  status: 'ACTIVE' | 'CLOSED_WIN' | 'CLOSED_LOSS'
  closedAt?: string
}

export interface TradeParametersData {
  entry: number
  stopLoss: number
  stopLossPips: number
  takeProfit: number
  takeProfitPips: number
  lotSize: number
  riskAmount: number
  riskPercent: number
  potentialProfit: number
  riskRewardRatio: number
}

export type SignalDirection = 'BUY' | 'SELL' | 'HOLD'
export type DecisionType = 'GO' | 'NO-GO' | 'GO_WITH_CAUTION'

export interface RiskGatePanelData {
  decision: 'GO' | 'NO-GO' | 'GO_WITH_CAUTION'
  equity: number
  drawdownPct: number       // 0.0–1.0
  openPositions: number
  maxPositions: number
  cautionNotes: string[]
  noGoReasons: string[]
  validatedEntry?: number
  validatedStopLoss?: number
  validatedTakeProfit?: number
}

export interface SignalAnalysisData {
  trendScore: number          // 0.0–1.0
  trendBias: string           // "Bullish"
  trendStrength: string       // "Sedang"
  trendRationale: string
  momentumScore: number
  momentumRSI: number
  momentumDirection: string   // "Naik"
  momentumRationale: string
  structureScore: number
  structurePattern: string    // "Support flip zone"
  structureRationale: string
  predictorScore: number      // 0–100 (e.g. 83)
  agreementScore: number      // 0.0–1.0
  confidenceScore: number     // 0.0–1.0 (for header badge)
  warnings: string[]
}

export type ActionState = 'enabled-go' | 'enabled-caution' | 'disabled-nogo' | 'processing'

export interface SignalHeroData {
  id: string
  pair: string
  timeframe: string
  signal: SignalDirection
  decision: DecisionType
  confidenceScore: number      // 0.0–1.0
  confluenceScore: number
  timestamp: string            // ISO string
  blockReason?: string         // shown when NO-GO
  cautionNotes?: string[]      // shown when GO_WITH_CAUTION
  parameters?: TradeParametersData
}

// --- API Response Types (mirrors C# domain models, camelCase JSON) ---

export interface MarketSnapshotResponse {
  pair: string
  timeframe: string
  currentPrice: number
  mA20_M15: number
  mA50_M15: number
  mA20_H1: number
  mA50_H1: number
  rsI14: number            // .NET CamelCase: RSI14 → rsI14
  rsiDirection: string     // RSIDirection → rsiDirection
  supportZone: string
  resistanceZone: string
  session: string
  capturedAt: string
}

export interface TrendAnalysisResponse {
  bias: string
  strength: string
  score: number
  htfAligned: boolean
  configuration: string
  scoreRationale: string
}

export interface MomentumAnalysisResponse {
  rsiValue: number         // .NET CamelCase: RSIValue → rsiValue
  rsiDirection: string     // RSIDirection → rsiDirection
  zone: string
  score: number
  scoreRationale: string
  divergence: string | null
}

export interface StructureAnalysisResponse {
  nearestSupport: number
  nearestResistance: number
  score: number
  scoreRationale: string
  candleConfirmed: boolean
  candlePattern: string
  pricePosition: string
}

export interface TradeParametersResponse {
  entry: number
  stopLoss: number
  stopLossPips: number
  takeProfit: number
  takeProfitPips: number
  lotSize: number
  riskAmount: number
  potentialProfit: number
  riskRewardRatio: number
}

export interface TradeSignalResponse {
  id: string
  runId: string
  pair: string
  timeframe: string
  signal: 'BUY' | 'SELL' | 'HOLD'
  confluenceScore: number       // int 0–100 from C#
  confidenceScore: number       // decimal 0–1 from C#
  snapshot: MarketSnapshotResponse
  trend: TrendAnalysisResponse
  momentum: MomentumAnalysisResponse
  structure: StructureAnalysisResponse
  parameters: TradeParametersResponse
  warnings: string[]
  timestamp: string
}

export interface RiskValidationResponse {
  decision: 'GO' | 'NO-GO' | 'GO_WITH_CAUTION'
  positionDecision: 'OPEN' | 'WATCHLIST' | 'REJECT'
  isGo: boolean
  validatedParameters: TradeParametersResponse | null
  cautionNotes: string[]
  noGoReasons: string[]
}

export interface TradePositionResponse {
  tradeId: string
  runId: string
  status: 'ACTIVE' | 'SKIPPED' | 'CLOSED_WIN' | 'CLOSED_LOSS'
  pair: string
  direction: 'BUY' | 'SELL' | 'HOLD'
  entry: number
  stopLoss: number
  takeProfit: number
  lotSize: number
  riskAmount: number
  potentialProfit: number
  riskReward: number
  floatingPnl: number
  floatingPnlPips: number
  openedAt: string | null
  closedAt: string | null
  mode: string
  skipReason: string | null
}

// --- API Request Types ---

export interface EvaluateRiskRequest {
  signalId: string
  finalDecision: string
  adjustedConfidence: number
  totalScore: number
  agreementScore: number
  equity: number
  openPositions: number
}

export interface ExecuteTradeRequest {
  signalId: string
  riskValidation: {
    decision: string
    positionDecision: string
    validatedParameters: TradeParametersResponse | null
    cautionNotes: string[]
    noGoReasons: string[]
  }
  peakEquity: number
  currentEquity: number
  mode: string
}
