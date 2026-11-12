import * as signalR from '@microsoft/signalr'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { LobbyState } from '../api/types'
import { clockOffset } from './clock'
import { newer } from './revision'

// Canlı lobi (DESIGN.md §8.12, §8.18): tek hub bağlantısı, sunucunun yolladığı tam durum. İstemci kural hesaplamaz;
// niyet gönderir (Ready, Ban, Pick, SwapRoles, LockLineup), sonucu LobbyUpdated'dan görür.

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'failed'

/** Hamlenin cevabı (sadece hamleyi yapana; yeni durum herkese LobbyUpdated ile). */
export interface ActionResult {
  ok: boolean
  error: string | null
  message: string | null
}

/** MatchReady: oynanan maçın dondurulmuş belgesi (§8.16). 5b sadece kimliğini kullanır; oynatıcı 5c'de. */
export interface MatchPayload {
  version: number
  id: string
  gameNumber: number
  [key: string]: unknown
}

export type HubMethod = 'Ready' | 'Ban' | 'Pick' | 'SwapRoles' | 'LockLineup'

// Kopunca yeniden dene: hükmen süresi (2 dk) boyunca sık, sonra seyrek; 10 dk sonra vazgeç.
// Varsayılan politika 4 denemede (~30 sn) bırakıyordu: kısa bir Wi-Fi kopması seriyi kaybettirebilirdi.
const retryPolicy: signalR.IRetryPolicy = {
  nextRetryDelayInMilliseconds: ({ elapsedMilliseconds, previousRetryCount }) => {
    if (elapsedMilliseconds > 10 * 60_000) return null
    if (elapsedMilliseconds > 2 * 60_000) return 10_000
    return [0, 1000, 2000][previousRetryCount] ?? 3000
  },
}

export function useLobbyConnection(token: string) {
  const [lobby, setLobby] = useState<LobbyState | null>(null)
  const [status, setStatus] = useState<ConnectionStatus>('connecting')
  const [closed, setClosed] = useState(false)
  const [offsetMs, setOffsetMs] = useState(0)
  const [matches, setMatches] = useState<MatchPayload[]>([])
  const connectionRef = useRef<signalR.HubConnection | null>(null)

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      // WebSocket başlık gönderemez: token sorgu parametresiyle gider (§8.4 madde 7; loglara yazılmaz).
      .withUrl('/hubs/draft', { accessTokenFactory: () => token })
      .withAutomaticReconnect(retryPolicy)
      .configureLogging(signalR.LogLevel.Warning)
      .build()
    connectionRef.current = connection

    connection.on('LobbyUpdated', (state: LobbyState) => {
      // Saat farkı her durumda yeniden ölçülür (geri sayım sunucu saatine göre).
      setOffsetMs(clockOffset(state.serverNow, Date.now()))
      setLobby((current) => (newer(current, state) ? state : current))
    })
    connection.on('MatchReady', (match: MatchPayload) => {
      setMatches((current) => (current.some((m) => m.id === match.id) ? current : [...current, match]))
    })
    connection.on('LobbyClosed', () => {
      setClosed(true)
      void connection.stop()
    })

    connection.onreconnecting(() => setStatus('reconnecting'))
    // Yeniden bağlanınca sunucu güncel durumu kendiliğinden yollar (OnConnectedAsync).
    connection.onreconnected(() => setStatus('connected'))
    connection.onclose(() => setStatus((s) => (s === 'connected' || s === 'reconnecting' ? 'failed' : s)))

    connection.start().then(
      () => setStatus('connected'),
      // 401 (token geçersiz) ya da sunucu bağlantıyı kapattı (koltuk yok, lobi kapandı).
      () => setStatus('failed'),
    )

    return () => {
      connectionRef.current = null
      void connection.stop()
    }
  }, [token])

  /**
   * Hamle gönderir. Her hamlenin bir kimliği var (actionId): bağlantı tam bu anda koparsa yeniden bağlanınca aynı
   * kimlikle bir kez daha denenir, sunucu ikinci kez uygulamaz (§8.12 tekrar koruması).
   */
  const act = useCallback(async (method: HubMethod, ...args: string[]): Promise<ActionResult> => {
    const connection = connectionRef.current
    if (!connection) return { ok: false, error: 'not_connected', message: 'Bağlantı yok.' }

    const actionId = crypto.randomUUID()
    try {
      return await connection.invoke<ActionResult>(method, ...args, actionId)
    } catch {
      // Bağlantı ayaktaysa hata sunucudan (beklenmeyen istisna): aynı hamleyi tekrar göndermenin anlamı yok.
      if (connection.state === signalR.HubConnectionState.Connected) {
        return { ok: false, error: 'server_error', message: 'Sunucu hamleyi işleyemedi.' }
      }
      if (!(await waitConnected(connection))) {
        return { ok: false, error: 'not_connected', message: 'Bağlantı koptu. Yeniden bağlanınca tekrar dene.' }
      }
      try {
        return await connection.invoke<ActionResult>(method, ...args, actionId)
      } catch {
        return { ok: false, error: 'not_connected', message: 'Hamle gönderilemedi.' }
      }
    }
  }, [])

  return { lobby, status, closed, offsetMs, matches, act }
}

// Bağlantı yeniden kurulana kadar en fazla 10 sn bekler.
function waitConnected(connection: signalR.HubConnection): Promise<boolean> {
  if (connection.state === signalR.HubConnectionState.Connected) return Promise.resolve(true)
  return new Promise((resolve) => {
    const started = Date.now()
    const timer = setInterval(() => {
      if (connection.state === signalR.HubConnectionState.Connected) {
        clearInterval(timer)
        resolve(true)
      } else if (Date.now() - started > 10_000 || connection.state === signalR.HubConnectionState.Disconnected) {
        clearInterval(timer)
        resolve(false)
      }
    }, 200)
  })
}
