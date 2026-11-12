import { useState } from 'react'
import { Link } from 'react-router-dom'
import type { Champion, Game, LobbyState, Seat } from '../api/types'
import { Countdown } from '../components/Countdown'
import { useChampions } from '../live/champions'
import { mySide } from '../live/draft'
import { loadRevealed, saveRevealed, visibleScore } from '../live/reveal'
import { useLobbyConnection } from '../live/useLobbyConnection'
import { MatchPlayer } from '../match/MatchPlayer'
import type { Replay } from '../match/types'
import { useReplay } from '../match/useReplay'
import type { Session } from '../session'
import { DraftBoard } from './DraftBoard'
import { SwapBoard } from './SwapBoard'

const FORFEIT_MS = 2 * 60_000 // Domain/Lobby.ForfeitAfter

// Canlı lobi (DESIGN.md §8.18): hub'dan gelen durum neyse o çizilir. Draft varsa draft/swap, yoksa seri paneli.
export function LobbyScreen({ code, session }: { code: string; session: Session }) {
  const { lobby, status, closed, offsetMs, matches, act } = useLobbyConnection(session.token)
  const champions = useChampions()
  const [revealed, setRevealed] = useState(() => loadRevealed(code))
  // Elle açılan eski maç (izlenmiş olanlar); izlenmemiş maç varsa o kendiliğinden açılır.
  const [watching, setWatching] = useState<number | null>(null)

  function reveal(count: number) {
    setRevealed(count)
    saveRevealed(code, count)
  }

  if (closed) return <p className="card">Lobi kapandı (uzun süre hareket yoktu ya da iki oyuncu da ayrıldı).</p>
  if (!lobby || !champions) {
    return <p className="muted">{status === 'failed' ? 'Lobiye bağlanılamadı. Sayfayı yenile.' : 'Bağlanıyor…'}</p>
  }

  const me = mySide(lobby, session.seat)
  const score = visibleScore(lobby, revealed)
  const draft = lobby.draft
  // Oynanmış ama izlenmemiş ilk maç: MatchReady gelince iki oyuncu da oynatıcıya geçer (skor bitince açılır, §8.15).
  const pending = revealed < lobby.games.length ? revealed : null
  const viewing = draft ? null : (watching ?? pending)

  return (
    <section className="lobby">
      <header className="lobby-head">
        <strong>
          {lobby.format.toUpperCase()}
          {lobby.fearless && ' · Fearless'}
        </strong>
        <span>
          Skor: A {score.winsA} – {score.winsB} B
          {revealed < lobby.games.length && <span className="muted small"> (izlenmemiş maç var)</span>}
        </span>
        <Seats lobby={lobby} you={session.seat} offsetMs={offsetMs} />
        {status !== 'connected' && <span className="error small">Bağlantı koptu, yeniden bağlanıyor…</span>}
      </header>

      {!lobby.isFull && <Invite code={code} />}

      {draft && draft.phase !== 'swap' && draft.phase !== 'completed' && (
        <DraftBoard draft={draft} me={me} champions={champions} offsetMs={offsetMs} act={act} />
      )}
      {draft?.phase === 'swap' && (
        <SwapBoard draft={draft} me={me} champions={champions} offsetMs={offsetMs} act={act} />
      )}
      {viewing !== null && (
        <GameViewer
          key={lobby.games[viewing].match}
          game={lobby.games[viewing]}
          known={matches.find((m) => m.id === lobby.games[viewing].match)}
          champions={champions}
          you={session.seat}
          onFinished={() => {
            // Bitince skor açılır; oynatıcı karneyle açık kalır (yoksa "izlenmemiş" listesi boşalınca hemen kapanırdı).
            if (viewing === revealed) {
              setWatching(viewing)
              reveal(revealed + 1)
            }
          }}
          onClose={watching !== null || viewing < revealed ? () => setWatching(null) : undefined}
        />
      )}
      {!draft && viewing === null && lobby.isFull && (
        <SeriesPanel lobby={lobby} you={session.seat} revealed={revealed} onWatch={setWatching} act={act} />
      )}
    </section>
  )
}

function Seats({ lobby, you, offsetMs }: { lobby: LobbyState; you: Seat; offsetMs: number }) {
  // Hükmen sayacı sadece seri başladıysa (Domain: Series.HasStarted) ve rakip bağlıysa işler.
  const started = lobby.games.length > 0 || lobby.draft !== null
  return (
    <span className="seats">
      {lobby.seats.map((s) => {
        const other = lobby.seats.find((o) => o.seat !== s.seat)
        const forfeitAt =
          started && !lobby.winner && s.absentSince && other?.connected
            ? new Date(Date.parse(s.absentSince) + FORFEIT_MS).toISOString()
            : null
        return (
          <span key={s.seat} className={s.connected ? 'seat on' : 'seat'}>
            {s.seat}
            {s.seat === you && ' (sen)'}: {s.connected ? 'bağlı' : 'bağlı değil'}
            {forfeitAt && (
              <>
                {' '}
                · hükmen <Countdown deadline={forfeitAt} offsetMs={offsetMs} /> sn
              </>
            )}
          </span>
        )
      })}
    </span>
  )
}

function Invite({ code }: { code: string }) {
  const url = `${window.location.origin}/l/${code}`
  return (
    <div className="card invite">
      <p>Rakip bekleniyor. Bu linki gönder:</p>
      <div className="row">
        <input readOnly value={url} onFocus={(e) => e.target.select()} />
        <button onClick={() => void navigator.clipboard?.writeText(url)}>Kopyala</button>
      </div>
    </div>
  )
}

function SeriesPanel(props: {
  lobby: LobbyState
  you: Seat
  revealed: number
  onWatch: (index: number) => void
  act: ReturnType<typeof useLobbyConnection>['act']
}) {
  const { lobby, you, revealed, onWatch, act } = props
  const [error, setError] = useState<string | null>(null)
  const score = visibleScore(lobby, revealed)
  const iAmReady = lobby.ready.includes(you)
  const finished = lobby.winner !== null

  async function ready() {
    const result = await act('Ready')
    setError(result.ok ? null : result.message)
  }

  return (
    <div className="card series">
      {lobby.games.length > 0 && (
        <ol className="games">
          {lobby.games.map((g, i) => (
            <li key={g.match}>
              Maç {g.number}: {g.blue === you ? 'mavi' : 'kırmızı'} taraftaydın
              {i < revealed && <b> · {g.winner === you ? 'kazandın' : 'kaybettin'}</b>}
              <button className="link" onClick={() => onWatch(i)}>
                tekrar izle
              </button>
              {/* Paylaşım linki: lobisiz tekrar izleme (token istemez). */}
              <Link to={`/m/${g.match}`} target="_blank">
                link
              </Link>
            </li>
          ))}
        </ol>
      )}

      {finished && score.winner && (
        <h2>
          {score.winner === you ? 'Seriyi kazandın!' : 'Seriyi kaybettin.'}
          {lobby.endReason === 'forfeit' && <span className="muted small"> (hükmen)</span>}
        </h2>
      )}
      {finished && !score.winner && <p>Seri bitti. Sonucu görmek için maçları izle.</p>}

      {!finished && (
        <>
          <p>
            {lobby.games.length === 0 ? 'İlk maç' : `Maç ${lobby.gameNumber}`}: bu maçta{' '}
            <b className={mySide(lobby, you)}>{mySide(lobby, you) === 'blue' ? 'mavisin' : 'kırmızısın'}</b>.
          </p>
          <button className="primary" disabled={iAmReady} onClick={ready}>
            {iAmReady ? 'Hazırsın, rakip bekleniyor' : 'Hazırım'}
          </button>
          {lobby.ready.length > 0 && !iAmReady && <span className="muted small"> Rakip hazır.</span>}
        </>
      )}
      {error && <p className="error">{error}</p>}
    </div>
  )
}

// Oynanmış maçın oynatıcısı: belge MatchReady'den (elde) ya da GET'ten.
function GameViewer(props: {
  game: Game
  known: Replay | undefined
  champions: Map<string, Champion>
  you: Seat
  onFinished: () => void
  onClose?: () => void
}) {
  const { game, known, champions, you, onFinished, onClose } = props
  const { replay, error } = useReplay(game.match, known)
  return (
    <div className="card">
      <div className="row viewer-head">
        <h2>Maç {game.number}</h2>
        {onClose && <button onClick={onClose}>Kapat</button>}
      </div>
      {error && <p className="error">{error}</p>}
      {!replay && !error && <p className="muted">Maç yükleniyor…</p>}
      {replay && <MatchPlayer replay={replay} champions={champions} you={you} onFinished={onFinished} />}
    </div>
  )
}
