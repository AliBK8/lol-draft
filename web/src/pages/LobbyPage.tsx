import { useCallback, useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api, ApiError } from '../api/http'
import type { Ticket } from '../api/types'
import { LobbyScreen } from '../screens/LobbyScreen'
import { clearRejoinToken, loadRejoinToken, loadSession, saveSession, type Session } from '../session'

// Davet linki (/l/:code). Bu sekmede token varsa canlı lobiye bağlanır. Token yok ama koltuk anahtarı varsa
// (sekmeyi kapatıp linki yeniden açan oyuncu, §8.24) koltuğunu kendiliğinden geri alır. İkisi de yoksa "Katıl".
// Durum hub'dan gelir (bağlanınca sunucu güncel durumu yollar, §8.18); GET gerekmez.
export function LobbyPage() {
  const { code = '' } = useParams()
  const [session, setSession] = useState<Session | null>(() => loadSession(code))
  const [hasRejoin, setHasRejoin] = useState(() => loadRejoinToken(code) !== null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  // StrictMode efekti iki kez çalıştırır: ikinci istek aynı (artık kullanılmış) anahtarı yollar, sunucu reddeder
  // ve istemci anahtarı silerdi. Kendiliğinden geri dönüş bir kez denenir.
  const autoTried = useRef(false)

  const enter = useCallback((ticket: Ticket) => {
    saveSession(ticket)
    setSession({ seat: ticket.seat, token: ticket.token })
  }, [])

  const rejoin = useCallback(async () => {
    const rejoinToken = loadRejoinToken(code)
    if (!rejoinToken) {
      setHasRejoin(false)
      return
    }
    setBusy(true)
    setError(null)
    try {
      enter(await api.rejoinLobby(code, rejoinToken))
    } catch (e) {
      // Sunucu anahtarı reddetti ya da lobi kapanmış: anahtar bir daha işe yaramaz. Ağ hatasında anahtar kalır.
      if (e instanceof ApiError && (e.code === 'rejoin_rejected' || e.code === 'lobby_not_found')) {
        clearRejoinToken(code)
        setHasRejoin(false)
      }
      setError(e instanceof ApiError ? e.message : 'Beklenmeyen bir hata oldu.')
    } finally {
      setBusy(false)
    }
  }, [code, enter])

  useEffect(() => {
    if (session || !hasRejoin || autoTried.current) return
    autoTried.current = true
    void rejoin()
  }, [session, hasRejoin, rejoin])

  async function join() {
    setBusy(true)
    setError(null)
    try {
      enter(await api.joinLobby(code))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Beklenmeyen bir hata oldu.')
    } finally {
      setBusy(false)
    }
  }

  if (session) return <LobbyScreen code={code} session={session} />

  return (
    <section className="card narrow">
      <h1>Seriye davet</h1>
      {hasRejoin ? (
        <>
          <p className="muted">Bu lobide bir koltuğun var.</p>
          <button className="primary" disabled={busy} onClick={() => void rejoin()}>
            {busy ? 'Koltuğuna dönülüyor…' : 'Koltuğuna dön'}
          </button>
        </>
      ) : (
        <>
          <p className="muted">Bir arkadaşın seni LoL draft'ına çağırıyor.</p>
          <button className="primary" disabled={busy} onClick={join}>
            {busy ? 'Katılıyor…' : 'Katıl'}
          </button>
        </>
      )}
      {error && <p className="error">{error}</p>}
    </section>
  )
}
