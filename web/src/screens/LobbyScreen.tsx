import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { Champion, Game, LobbyState, Seat, Side } from '../api/types'
import { Countdown } from '../components/Countdown'
import { useChampions } from '../live/champions'
import { mySide } from '../live/draft'
import { loadRevealed, saveRevealed, visibleScore } from '../live/reveal'
import { WINS_TO_TAKE } from '../live/series'
import { useLobbyConnection } from '../live/useLobbyConnection'
import { MatchPlayer } from '../match/MatchPlayer'
import type { Replay } from '../match/types'
import { useReplay } from '../match/useReplay'
import { clearRejoinToken, clearSession, type Session } from '../session'
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

  // Lobi kapandı: token da koltuk anahtarı da artık işe yaramaz (linki yeniden açan "Katıl" görür, lobi yok der).
  useEffect(() => {
    if (!closed) return
    clearSession(code)
    clearRejoinToken(code)
  }, [closed, code])

  if (closed) {
    return (
      <section className="card narrow notice">
        <h2>Lobi kapandı</h2>
        <p className="muted">2 saat doldu, uzun süre hareket olmadı ya da iki oyuncu da ayrıldı.</p>
        <Link className="button primary" to="/">
          Yeni seri aç
        </Link>
      </section>
    )
  }
  if (!lobby || !champions) {
    return (
      <p className="muted notice-line">
        {status === 'failed' ? 'Lobiye bağlanılamadı. Sayfayı yenile.' : 'Bağlanıyor…'}
      </p>
    )
  }

  const me = mySide(lobby, session.seat)
  const score = visibleScore(lobby, revealed)
  const draft = lobby.draft
  // Oynanmış ama izlenmemiş ilk maç: MatchReady gelince iki oyuncu da oynatıcıya geçer (skor bitince açılır, §8.15).
  const pending = revealed < lobby.games.length ? revealed : null
  const viewing = draft ? null : (watching ?? pending)

  return (
    <section className="lobby">
      <SeriesBand
        lobby={lobby}
        you={session.seat}
        offsetMs={offsetMs}
        winsA={score.winsA}
        winsB={score.winsB}
        hidden={revealed < lobby.games.length}
        compact={draft !== null || viewing !== null}
      />
      {status !== 'connected' && <p className="conn-warn">Bağlantı koptu, yeniden bağlanıyor…</p>}

      {!lobby.isFull && <Invite code={code} />}

      {draft && draft.phase !== 'swap' && draft.phase !== 'completed' && (
        <DraftBoard key={lobby.gameNumber} draft={draft} me={me} champions={champions} offsetMs={offsetMs} act={act} />
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

// Seri bandı: solda A, ortada skor, sağda B. Draft ya da maç açıkken incelir (compact), ekranı onlara bırakır.
function SeriesBand(props: {
  lobby: LobbyState
  you: Seat
  offsetMs: number
  winsA: number
  winsB: number
  hidden: boolean
  compact: boolean
}) {
  const { lobby, you, offsetMs, winsA, winsB, hidden, compact } = props
  return (
    <header className={compact ? 'series-band compact' : 'series-band'}>
      <SeatCard lobby={lobby} seat="A" you={you} offsetMs={offsetMs} />
      <div className="series-center">
        <div className="chips">
          <span className="chip">{lobby.format.toUpperCase()}</span>
          {lobby.fearless && <span className="chip accent">Fearless</span>}
        </div>
        <div className="series-score" aria-label={`Skor: A ${winsA}, B ${winsB}`}>
          <b>{winsA}</b>
          <span>–</span>
          <b>{winsB}</b>
        </div>
        {!compact && (
          <small className="muted">
            {hidden ? 'İzlenmemiş maç var' : `İlk ${WINS_TO_TAKE[lobby.format]} galibiyet seriyi alır`}
          </small>
        )}
      </div>
      <SeatCard lobby={lobby} seat="B" you={you} offsetMs={offsetMs} />
    </header>
  )
}

function SeatCard({ lobby, seat, you, offsetMs }: { lobby: LobbyState; seat: Seat; you: Seat; offsetMs: number }) {
  const state = lobby.seats.find((s) => s.seat === seat)
  const other = lobby.seats.find((s) => s.seat !== seat)
  // Hükmen sayacı sadece seri başladıysa (Domain: Series.HasStarted) ve rakip bağlıysa işler.
  const started = lobby.games.length > 0 || lobby.draft !== null
  const forfeitAt =
    state && started && !lobby.winner && state.absentSince && other?.connected
      ? new Date(Date.parse(state.absentSince) + FORFEIT_MS).toISOString()
      : null
  // Bu maçta hangi renk (renkler her maç değişir); seri bittiyse renk yok.
  const side: Side | null = lobby.winner ? null : lobby.blueTeam === seat ? 'blue' : 'red'
  const classes = ['seat-card', seat === 'B' ? 'right' : '', side ?? '', state?.connected ? 'on' : '', state ? '' : 'empty']

  return (
    <div className={classes.join(' ').replace(/\s+/g, ' ').trim()}>
      <span className="seat-avatar">{seat}</span>
      <span className="seat-info">
        <strong>{!state ? 'Boş koltuk' : seat === you ? 'Sen' : 'Rakip'}</strong>
        <small>
          <i className="dot" aria-hidden="true" />
          {!state ? 'Rakip bekleniyor' : state.connected ? 'Bağlı' : 'Bağlı değil'}
          {forfeitAt && (
            <>
              {' '}
              · hükmen <Countdown deadline={forfeitAt} offsetMs={offsetMs} /> sn
            </>
          )}
        </small>
      </span>
      {side && state && <span className={`side-chip ${side}`}>{side === 'blue' ? 'Mavi' : 'Kırmızı'}</span>}
    </div>
  )
}

function Invite({ code }: { code: string }) {
  const url = `${window.location.origin}/l/${code}`
  const [copied, setCopied] = useState(false)
  const input = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!copied) return
    const timer = setTimeout(() => setCopied(false), 2000)
    return () => clearTimeout(timer)
  }, [copied])

  // Pano API'si sadece güvenli bağlamda (HTTPS/localhost) var; yoksa ya da reddederse metin seçilir, elle kopyalanır.
  async function copy() {
    try {
      await navigator.clipboard.writeText(url)
      setCopied(true)
    } catch {
      input.current?.select()
    }
  }

  return (
    <div className="card invite">
      <div className="invite-head">
        <span className="pulse" aria-hidden="true" />
        <div>
          <strong>Rakip bekleniyor</strong>
          <p className="muted small">Bu linki arkadaşına gönder; açan rakip koltuğa oturur.</p>
        </div>
      </div>
      <div className="invite-link">
        <input ref={input} readOnly value={url} onFocus={(e) => e.target.select()} aria-label="Davet linki" />
        <button className={copied ? 'copied' : 'primary'} onClick={() => void copy()}>
          {copied ? 'Kopyalandı ✓' : 'Linki kopyala'}
        </button>
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
  const side = mySide(lobby, you)

  async function ready() {
    const result = await act('Ready')
    setError(result.ok ? null : result.message)
  }

  return (
    <div className="series">
      {finished && score.winner && (
        <div className={score.winner === you ? 'card series-result win' : 'card series-result loss'}>
          <h2>{score.winner === you ? 'Seriyi kazandın!' : 'Seriyi kaybettin.'}</h2>
          <p className="muted">
            {score.winsA} – {score.winsB}
            {lobby.endReason === 'forfeit' && ' · hükmen'}
          </p>
          <Link className="button primary" to="/">
            Yeni seri aç
          </Link>
        </div>
      )}
      {finished && !score.winner && (
        <div className="card series-result">
          <p>Seri bitti. Sonucu görmek için maçları izle.</p>
        </div>
      )}

      {!finished && (
        <div className={`card next-game ${side}`}>
          <div>
            <span className="eyebrow">{lobby.games.length === 0 ? 'İlk maç' : `Maç ${lobby.gameNumber}`}</span>
            <h2>
              Bu maçta <b className={side}>{side === 'blue' ? 'mavisin' : 'kırmızısın'}</b>
            </h2>
            <p className="muted small">
              İkiniz de hazır olunca draft başlar. {side === 'blue' ? 'İlk ban ve ilk pick sende.' : 'İlk ban ve ilk pick rakipte.'}
            </p>
          </div>
          <div className="ready-box">
            <button className="primary big" disabled={iAmReady} onClick={ready}>
              {iAmReady ? 'Hazırsın, rakip bekleniyor' : 'Hazırım'}
            </button>
            {lobby.ready.length > 0 && !iAmReady && <span className="chip ok">Rakip hazır</span>}
          </div>
        </div>
      )}
      {error && <p className="error">{error}</p>}

      {lobby.games.length > 0 && (
        <div className="card">
          <h3 className="section-title">Maçlar</h3>
          <ol className="games">
            {lobby.games.map((g, i) => {
              const gameSide: Side = g.blue === you ? 'blue' : 'red'
              const result = i >= revealed ? 'hidden' : g.winner === you ? 'win' : 'loss'
              return (
                <li key={g.match} className="game-row">
                  <span className="game-no">Maç {g.number}</span>
                  <span className={`side-chip ${gameSide}`}>{gameSide === 'blue' ? 'Mavi' : 'Kırmızı'}</span>
                  <span className={`result ${result}`}>
                    {result === 'hidden' ? 'İzlenmedi' : result === 'win' ? 'Kazandın' : 'Kaybettin'}
                  </span>
                  <span className="game-actions">
                    <button onClick={() => onWatch(i)}>{result === 'hidden' ? 'İzle' : 'Tekrar izle'}</button>
                    {/* Paylaşım linki: lobisiz tekrar izleme (token istemez). */}
                    <Link to={`/m/${g.match}`} target="_blank" className="share">
                      Paylaşım linki
                    </Link>
                  </span>
                </li>
              )
            })}
          </ol>
        </div>
      )}
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
