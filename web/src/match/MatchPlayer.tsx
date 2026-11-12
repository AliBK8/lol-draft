import { useEffect, useMemo, useRef, useState } from 'react'
import type { Champion, Seat, Side } from '../api/types'
import { MapView } from './MapView'
import { MatchReport } from './MatchReport'
import { placement, type Placement } from './positions'
import { clock, endTime, GAME_SECONDS_PER_SECOND, roster, stateAt } from './timeline'
import type { Replay } from './types'

const SIDE_TR: Record<Side, string> = { blue: 'Mavi', red: 'Kırmızı' }
const SPEEDS = [1, 2, 4] as const

interface Props {
  replay: Replay
  champions: Map<string, Champion>
  /** Oyuncunun koltuğu (lobide); paylaşım sayfasında yok. */
  you?: Seat
  /** Oynatma sona ulaşınca bir kez (izlendi → skor açılır, §8.15). */
  onFinished?: () => void
}

// Maç oynatıcısı (DESIGN.md §7, §8.19): tek değişken oyun saati T; her karede T ilerler, ekran stateAt(T)'den çizilir.
export function MatchPlayer({ replay, champions, you, onFinished }: Props) {
  const end = useMemo(() => endTime(replay), [replay])
  const players = useMemo(() => roster(replay), [replay])
  const [t, setT] = useState(0)
  const [playing, setPlaying] = useState(true)
  const [speed, setSpeed] = useState<(typeof SPEEDS)[number]>(1)
  const [showMath, setShowMath] = useState(false)
  const finishedRef = useRef(false)

  // Oynatma döngüsü: gerçek geçen süre × hız × 20 oyun sn. Sekme arkadayken tarayıcı kareyi durdurur; dönünce
  // büyük sıçrama olmasın diye kare başına en fazla 0,1 sn sayılır.
  useEffect(() => {
    if (!playing) return
    let last: number | null = null
    let frame = requestAnimationFrame(function tick(now) {
      const dt = last === null ? 0 : Math.min(0.1, (now - last) / 1000)
      last = now
      setT((current) => Math.min(end, current + dt * speed * GAME_SECONDS_PER_SECOND))
      frame = requestAnimationFrame(tick)
    })
    return () => cancelAnimationFrame(frame)
  }, [playing, speed, end])

  const state = useMemo(() => stateAt(replay, t), [replay, t])

  useEffect(() => {
    if (state.finished && !finishedRef.current) {
      finishedRef.current = true
      setPlaying(false)
      onFinished?.()
    }
  }, [state.finished, onFinished])

  const placements = useMemo(() => {
    const map = new Map<string, { side: Side; placement: Placement }>()
    for (const [id, c] of players) {
      map.set(id, { side: c.side, placement: placement(replay.events, state, id, c.side, c.role, c.slot) })
    }
    return map
  }, [players, replay.events, state])

  const youSide: Side | null = you ? (replay.blueTeam === you ? 'blue' : 'red') : null
  const gold = Math.round(state.goldDiff / 100) * 100

  return (
    <div className="player">
      <div className="hud">
        <span className="blue">
          {SIDE_TR.blue}
          {youSide === 'blue' && ' (sen)'} <b>{state.kills.blue}</b>
        </span>
        <span className="clock">{clock(t)}</span>
        <span className="red">
          <b>{state.kills.red}</b> {SIDE_TR.red}
          {youSide === 'red' && ' (sen)'}
        </span>
        <span className="muted small">
          Altın:{' '}
          {gold === 0 ? 'eşit' : <b className={gold > 0 ? 'blue' : 'red'}>{`${gold > 0 ? 'Mavi' : 'Kırmızı'} +${Math.abs(gold).toLocaleString('tr-TR')}`}</b>}
          {' · '}Ejder {state.dragons.blue} / {state.dragons.red}
          {' · '}Kule {towersDown(state, 'red')} / {towersDown(state, 'blue')}
        </span>
      </div>

      <div className="player-main">
        <MapView state={state} events={replay.events} placements={placements} champions={champions} />
        <ol className="log">
          {state.log.map((e, i) => (
            <li key={`${e.t}-${i}`} className={`${'side' in e ? e.side : ''}${big(e.type) ? ' big' : ''}`}>
              <span className="muted">{clock(e.t)}</span> {e.text}
              {showMath && 'probability' in e && typeof e.probability === 'number' && (
                <span className="muted small"> %{Math.round(e.probability * 100)}</span>
              )}
            </li>
          ))}
        </ol>
      </div>

      <div className="controls">
        {!state.finished ? (
          <>
            <button onClick={() => setPlaying((p) => !p)}>{playing ? 'Duraklat' : 'Devam'}</button>
            {SPEEDS.map((s) => (
              <button key={s} className={speed === s ? 'on' : ''} onClick={() => setSpeed(s)}>
                {s}x
              </button>
            ))}
            <button onClick={() => setT(end)}>Sona atla</button>
          </>
        ) : (
          <button
            onClick={() => {
              setT(0)
              setPlaying(true)
            }}
          >
            Baştan izle
          </button>
        )}
        <label className="check small">
          <input type="checkbox" checked={showMath} onChange={(e) => setShowMath(e.target.checked)} />
          Matematiği göster
        </label>
      </div>

      {state.finished && <MatchReport replay={replay} champions={champions} you={you} />}
    </div>
  )
}

// HUD'daki kule sayısı: o tarafın yıktığı = rakibin kaybettiği.
function towersDown(state: ReturnType<typeof stateAt>, loser: Side): number {
  const lost = state.towers.lost[loser]
  return lost.top + lost.mid + lost.bot
}

const big = (type: string) => ['objective', 'nexus', 'tower', 'throw', 'skirmish', 'dragonSoul'].includes(type)
