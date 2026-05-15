'use client'

import { useEffect, useRef, useState } from 'react'
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { API_URL } from '@/lib/env'
import type {
  AccountHealthResponse,
  MifxStatusResponse,
  TradePositionResponse,
} from '@/lib/types'

interface StreamState {
  mifxStatus: MifxStatusResponse | null
  positions: TradePositionResponse[] | null
  accountHealth: AccountHealthResponse | null
  isConnected: boolean
}

/**
 * Real-time push dari backend via SignalR.
 * - Otomatis connect + auto-reconnect saat disconnect.
 * - Return state akan terupdate setiap EA push tick ke backend.
 * - Jika hub gagal connect: state tetap null → caller harus fallback ke polling REST.
 */
export function useDashboardStream(): StreamState {
  const [mifxStatus, setMifxStatus] = useState<MifxStatusResponse | null>(null)
  const [positions, setPositions] = useState<TradePositionResponse[] | null>(null)
  const [accountHealth, setAccountHealth] = useState<AccountHealthResponse | null>(null)
  const [isConnected, setIsConnected] = useState(false)
  const connRef = useRef<HubConnection | null>(null)

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hub/dashboard`)
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()

    conn.on('tick', (payload: MifxStatusResponse) => setMifxStatus(payload))
    conn.on('positions', (payload: TradePositionResponse[]) => setPositions(payload))
    conn.on('account', (payload: AccountHealthResponse) => setAccountHealth(payload))

    conn.onreconnected(() => setIsConnected(true))
    conn.onreconnecting(() => setIsConnected(false))
    conn.onclose(() => setIsConnected(false))

    conn.start()
      .then(() => setIsConnected(conn.state === HubConnectionState.Connected))
      .catch((err) => {
        console.warn('[SignalR] connect failed, akan fallback ke polling:', err)
        setIsConnected(false)
      })

    connRef.current = conn

    return () => {
      conn.stop().catch(() => {})
      connRef.current = null
    }
  }, [])

  return { mifxStatus, positions, accountHealth, isConnected }
}
