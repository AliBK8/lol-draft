import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/http'
import type { Format } from '../api/types'
import { saveSession } from '../session'

// Lobi aç: format + fearless → token → davet linkinin sayfasına (açan A koltuğunda).
export function HomePage() {
  const navigate = useNavigate()
  const [format, setFormat] = useState<Format>('bo3')
  const [fearless, setFearless] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Fearless + bo5 40'lık havuza sığmıyor (sunucu reddeder, DESIGN.md §8.3); seçtirmeyiz.
  const fearlessBlocked = format === 'bo5'

  async function create() {
    setBusy(true)
    setError(null)
    try {
      const ticket = await api.createLobby(format, fearless && !fearlessBlocked)
      saveSession(ticket)
      navigate(`/l/${ticket.code}`)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Beklenmeyen bir hata oldu.')
      setBusy(false)
    }
  }

  return (
    <section className="card narrow">
      <h1>Yeni seri</h1>
      <p className="muted">Lobiyi aç, linki arkadaşına at. Draft'ı siz yaparsınız, maçı motor oynar.</p>

      <fieldset className="choices">
        <legend>Format</legend>
        {(['bo1', 'bo3', 'bo5'] as const).map((f) => (
          <label key={f} className={format === f ? 'choice on' : 'choice'}>
            <input type="radio" name="format" value={f} checked={format === f} onChange={() => setFormat(f)} />
            {f.toUpperCase()}
          </label>
        ))}
      </fieldset>

      <label className="check">
        <input
          type="checkbox"
          checked={fearless && !fearlessBlocked}
          disabled={fearlessBlocked}
          onChange={(e) => setFearless(e.target.checked)}
        />
        Fearless (seçilen şampiyon serinin geri kalanında iki takıma da kapanır)
      </label>
      {fearlessBlocked && <p className="muted small">BO5 fearless için havuz yetmiyor (40 şampiyon).</p>}

      <button className="primary" disabled={busy} onClick={create}>
        {busy ? 'Açılıyor…' : 'Lobi aç'}
      </button>
      {error && <p className="error">{error}</p>}
    </section>
  )
}
