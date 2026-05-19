import { API_URL } from '@/lib/env'
import type {
  TradeSignalResponse,
  RiskValidationResponse,
  TradePositionResponse,
  EvaluateRiskRequest,
  ExecuteTradeRequest,
  CandleBar,
  ChartTimeframe,
  AccountHealthResponse,
  MifxStatusResponse,
  AuditEvent,
  SystemState,
  BacktestRunRequest,
  BacktestResult,
  PatternResponse,
  FvgDetectionResponse,
  SettingsResponse,
  SettingsUpdateRequest,
  AdaptiveStatsResponse,
  DynamicStructureResponse,
  AdaptiveEffectiveResponse,
  AdaptiveStateResponse,
} from '@/lib/types'

async function fetchApi<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })
  if (!res.ok) {
    // Coba parse error message dari JSON response backend
    try {
      const body = await res.json() as { error?: string }
      throw new Error(body.error ?? `API error ${res.status}`)
    } catch (parseErr) {
      if (parseErr instanceof SyntaxError) throw new Error(`API error ${res.status}`)
      throw parseErr
    }
  }
  return res.json() as Promise<T>
}

export async function analyzeSignal(
  pair: string,
  timeframe: string,
): Promise<TradeSignalResponse> {
  return fetchApi('/api/signal/analyze', {
    method: 'POST',
    body: JSON.stringify({ pair, timeframe }),
  })
}

export async function evaluateRisk(
  req: EvaluateRiskRequest,
): Promise<RiskValidationResponse> {
  return fetchApi('/api/risk/evaluate', {
    method: 'POST',
    body: JSON.stringify(req),
  })
}

export async function executeTrade(
  req: ExecuteTradeRequest,
): Promise<TradePositionResponse> {
  return fetchApi('/api/trade/execute', {
    method: 'POST',
    body: JSON.stringify(req),
  })
}

export async function getPositionStatus(
  pair: string,
): Promise<TradePositionResponse | null> {
  const res = await fetch(`${API_URL}/api/position/${pair}`, {
    headers: { 'Content-Type': 'application/json' },
  })
  if (res.status === 204) return null
  if (!res.ok) throw new Error(`API ${res.status}`)
  return res.json() as Promise<TradePositionResponse>
}

export async function getAllPositions(): Promise<TradePositionResponse[]> {
  return fetchApi('/api/position')
}

export async function getCandles(
  pair: string = 'EURUSD',
  timeframe: ChartTimeframe = 'M15',
  count: number = 200,
): Promise<CandleBar[]> {
  return fetchApi(
    `/api/market/candles?pair=${pair}&timeframe=${timeframe}&count=${count}`,
  )
}

export async function getAccountHealth(): Promise<AccountHealthResponse> {
  return fetchApi('/api/account')
}

export async function getMifxStatus(): Promise<MifxStatusResponse> {
  return fetchApi('/api/mifx/status')
}

export async function deployEa(): Promise<{
  success: boolean
  message: string
  compiled: boolean
  deployedPath: string
  compileLog: string
}> {
  return fetchApi('/api/ea/deploy', { method: 'POST' })
}

export async function closePosition(
  tradeId: string,
  outcome: 'WIN' | 'LOSS',
  exitPrice: number,
): Promise<TradePositionResponse> {
  return fetchApi(`/api/position/${tradeId}/close`, {
    method: 'POST',
    body: JSON.stringify({ outcome, exitPrice }),
  })
}

// One-click close: backend auto-detect outcome dari floating PnL + pakai harga MIFX terbaru.
// Untuk live mode, broker close otomatis dijalankan oleh ClosePositionHandler.
export async function closePositionMarket(tradeId: string): Promise<TradePositionResponse> {
  return fetchApi(`/api/position/${tradeId}/close-market`, { method: 'POST' })
}

// ── System control (kill switch) ────────────────────────────────────────
export async function getSystemState(): Promise<SystemState> {
  return fetchApi('/api/system/state')
}

export async function haltSystem(
  reason: string,
  closeAll: boolean = true,
): Promise<{ halted: boolean; closedCount: number; closedTickets: string[]; failures: string[] }> {
  return fetchApi(`/api/system/halt?closeAll=${closeAll}`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  })
}

export async function resumeSystem(): Promise<{ halted: boolean }> {
  return fetchApi('/api/system/resume', { method: 'POST' })
}

// ── Audit log ───────────────────────────────────────────────────────────
export async function getAuditEvents(
  limit: number = 200,
  type?: string,
): Promise<AuditEvent[]> {
  const qs = type ? `?limit=${limit}&type=${type}` : `?limit=${limit}`
  return fetchApi(`/api/audit${qs}`)
}

// ── Backtest ───────────────────────────────────────────────────────────
export async function runBacktest(req: BacktestRunRequest): Promise<BacktestResult> {
  return fetchApi('/api/backtest/run', {
    method: 'POST',
    body: JSON.stringify(req),
  })
}

// ── Candlestick pattern detection (per-TF) ──────────────────────────────
export async function fetchPatterns(pair: string = 'EURUSD'): Promise<PatternResponse> {
  return fetchApi(`/api/pattern/detect?pair=${encodeURIComponent(pair)}`)
}

// ── Fair Value Gap (FVG) zones per-TF ───────────────────────────────────
export async function fetchFvg(pair: string = 'EURUSD'): Promise<FvgDetectionResponse> {
  return fetchApi(`/api/pattern/fvg?pair=${encodeURIComponent(pair)}`)
}

// ── Settings (safety thresholds config) ─────────────────────────────────
export async function getSettings(): Promise<SettingsResponse> {
  return fetchApi('/api/settings')
}

export async function updateSettings(req: SettingsUpdateRequest): Promise<SettingsResponse> {
  return fetchApi('/api/settings', {
    method: 'POST',
    body: JSON.stringify(req),
  })
}

// ── Adaptive learning stats (Phase 1 observe) ──────────────────────────
export async function fetchAdaptiveStats(window = 30): Promise<AdaptiveStatsResponse> {
  return fetchApi(`/api/adaptive/stats?window=${window}`)
}

// ── Dynamic Structure (swing pivots + trendlines) ──────────────────────
export async function fetchDynamicStructure(
  pair: string = 'EURUSD',
  timeframe: string = 'M15',
): Promise<DynamicStructureResponse> {
  return fetchApi(`/api/structure/dynamic?pair=${encodeURIComponent(pair)}&timeframe=${timeframe}`)
}

// ── Adaptive effective thresholds (baseline + per-regime override) ──────
export async function fetchAdaptiveEffective(): Promise<AdaptiveEffectiveResponse> {
  return fetchApi('/api/adaptive/effective')
}

// ── Adaptive state (overrides + audit history) ──────────────────────────
export async function fetchAdaptiveState(): Promise<AdaptiveStateResponse> {
  return fetchApi('/api/adaptive/state')
}

export async function rollbackAdaptive(snapshotId: string, requestedBy = 'user'): Promise<{ success: boolean; message: string }> {
  return fetchApi(`/api/adaptive/rollback/${encodeURIComponent(snapshotId)}`, {
    method: 'POST',
    body: JSON.stringify({ requestedBy }),
  })
}

export async function setAdaptiveMasterDisabled(disabled: boolean): Promise<void> {
  await fetchApi('/api/adaptive/disable', {
    method: 'POST',
    body: JSON.stringify({ disabled }),
  })
}

export async function setAdaptiveActionDisabled(actionName: string, disabled: boolean): Promise<void> {
  await fetchApi(`/api/adaptive/action/${encodeURIComponent(actionName)}/disable`, {
    method: 'POST',
    body: JSON.stringify({ disabled }),
  })
}
