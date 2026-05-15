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
