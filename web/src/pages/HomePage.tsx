import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/http'
import type { Format } from '../api/types'
import { WINS_TO_TAKE } from '../live/series'
import { saveSession } from '../session'
import { HomeBackdrop } from './HomeBackdrop'

const FORMATS: { id: Format; label: string }[] = [
  { id: 'bo1', label: 'Tek maç' },
  { id: 'bo3', label: `İlk ${WINS_TO_TAKE.bo3} galibiyet` },
  { id: 'bo5', label: `İlk ${WINS_TO_TAKE.bo5} galibiyet` },
]

const STEPS: [string, string][] = [
  ['Lobiyi aç', 'Formatı seç, tek tıkla açılır. Kayıt yok.'],
  ['Linki at', 'Linki açan arkadaşın rakip koltuğa oturur.'],
  ['Draft yap', 'Turnuva sırası: ban, pick, swap. Her adım 30 sn.'],
  ['Maçı izle', 'Motor maçı oynar, siz haritada izlersiniz.'],
]

// Lobi aç: format + fearless → token → davet linkinin sayfasına (açan A koltuğunda).
export function HomePage() {
  const navigate = useNavigate()
  const [format, setFormat] = useState<Format>('bo3')
  const [fearless, setFearless] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Her format fearless'la açılabilir: 170'lik havuzda bo5'in son maçından önce 40 kilit olur,
  // geriye 130 şampiyon kalır (en az 20 gerekir; sunucu da denetler, DESIGN.md §8.3).
  async function create() {
    setBusy(true)
    setError(null)
    try {
      const ticket = await api.createLobby(format, fearless)
      saveSession(ticket)
      navigate(`/l/${ticket.code}`)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Beklenmeyen bir hata oldu.')
      setBusy(false)
    }
  }

  return (
    <div className="home">
      <HomeBackdrop />

      <section className="home-hero">
        <span className="eyebrow">Turnuva draft'ı · arkadaşınla</span>
        <h1>
          Draft'ı siz yapın,
          <br />
          <span className="grad">maçı motor oynasın.</span>
        </h1>
        <p className="lead">
          Ban, pick, bo1/bo3/bo5 ve fearless. Draft bitince maç simüle edilir; şampiyonları haritada, olayları akışta
          izlersiniz. Sonunda "neden kazandın?" karnesi.
        </p>
        <ol className="steps">
          {STEPS.map(([title, text], i) => (
            <li key={title}>
              <span className="step-no">{i + 1}</span>
              <span>
                <strong>{title}</strong>
                <small>{text}</small>
              </span>
            </li>
          ))}
        </ol>
      </section>

      <section className="card create">
        <h2>Yeni seri</h2>

        <fieldset className="formats">
          <legend>Format</legend>
          {FORMATS.map((f) => (
            <label key={f.id} className={format === f.id ? 'format on' : 'format'}>
              <input
                className="sr-only"
                type="radio"
                name="format"
                value={f.id}
                checked={format === f.id}
                onChange={() => setFormat(f.id)}
              />
              <strong>{f.id.toUpperCase()}</strong>
              <small>{f.label}</small>
            </label>
          ))}
        </fieldset>

        <label className={fearless ? 'toggle-card on' : 'toggle-card'}>
          <input
            className="sr-only"
            type="checkbox"
            checked={fearless}
            onChange={(e) => setFearless(e.target.checked)}
          />
          <span className="toggle-text">
            <strong>Fearless</strong>
            <small>Seçilen şampiyon serinin geri kalanında iki takıma da kapanır.</small>
          </span>
          <span className="switch" aria-hidden="true" />
        </label>

        <button className="primary big" disabled={busy} onClick={create}>
          {busy ? 'Açılıyor…' : 'Lobi aç'}
        </button>
        <p className="muted small hint">Açan A koltuğuna oturur. Lobi en fazla 2 saat açık kalır.</p>
        {error && <p className="error">{error}</p>}
      </section>
    </div>
  )
}
