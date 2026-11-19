import { useEffect, useMemo, useRef, useState } from 'react'
import type { Champion, Seat, Side } from '../api/types'
import { EventLog } from './EventLog'
import { MapView } from './MapView'
import { MatchReport } from './MatchReport'
import { buildTracks, poseAt, type Pose } from './movement'
import { scoreboard } from './scoreboard'
import { ScoreBand, TeamRoster } from './ScoreBand'
import { endTime, GAME_SECONDS_PER_SECOND, roster, stateAt } from './timeline'
import type { MatchEvent, Replay } from './types'

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

  // Güzergâhlar maç başına bir kez (movement.ts: hedeflere yürüyüş); her karede sadece T'nin noktası okunur.
  const tracks = useMemo(() => buildTracks(replay), [replay])
  const placements = useMemo(() => {
    const map = new Map<string, { side: Side; placement: Pose }>()
    for (const [id, c] of players) {
      const track = tracks.get(id)
      if (track) map.set(id, { side: c.side, placement: poseAt(track, t) })
    }
    return map
  }, [players, tracks, t])

  // Skor bandı ve kadrolar her karede T'den (scoreboard.ts); log kartlarının anahtarı olayın belgedeki sırası.
  const board = useMemo(() => scoreboard(replay, state), [replay, state])
  const order = useMemo(() => new Map<MatchEvent, number>(replay.events.map((e, i) => [e, i])), [replay])
  const youSide: Side | null = you ? (replay.blueTeam === you ? 'blue' : 'red') : null

  return (
    <div className="player">
      <ScoreBand board={board} t={t} you={youSide} />

      <div className="player-main">
        <TeamRoster lines={board.champions.filter((c) => c.side === 'blue')} side="blue" champions={champions} />
        <MapView state={state} events={replay.events} placements={placements} champions={champions} />
        <TeamRoster lines={board.champions.filter((c) => c.side === 'red')} side="red" champions={champions} />
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

      <EventLog log={state.log} order={order} champions={champions} showMath={showMath} />

      {state.finished && <MatchReport replay={replay} champions={champions} you={you} />}
    </div>
  )
}
