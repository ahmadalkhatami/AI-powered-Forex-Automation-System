import { API_URL } from '@/lib/env'
import type {
  TradeSignalResponse,
  RiskValidationResponse,
  TradePositionResponse,
  EvaluateRiskRequest,
  ExecuteTradeRequest,
} from '@/lib/types'

async function fetchApi<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`API ${res.status}: ${text}`)
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
