import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { api, ApiError } from '../api/http'
import { LobbyScreen } from '../screens/LobbyScreen'
import { loadSession, saveSession, type Session } from '../session'

// Davet linki (/l/:code). Bu sekmede token varsa canlı lobiye bağlanır; yoksa "Katıl".
// Durum hub'dan gelir (bağlanınca sunucu güncel durumu yollar, §8.18); GET gerekmez.
export function LobbyPage() {
  const { code = '' } = useParams()
  const [session, setSession] = useState<Session | null>(() => loadSession(code))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function join() {
    setBusy(true)
    setError(null)
    try {
      const ticket = await api.joinLobby(code)
      saveSession(ticket)
      setSession({ seat: ticket.seat, token: ticket.token })
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
      <p className="muted">Bir arkadaşın seni LoL draft'ına çağırıyor.</p>
      <button className="primary" disabled={busy} onClick={join}>
        {busy ? 'Katılıyor…' : 'Katıl'}
      </button>
      {error && <p className="error">{error}</p>}
    </section>
  )
}
