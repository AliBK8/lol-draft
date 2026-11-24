import { useEffect, useMemo, useRef, useState } from 'react'
import type { Champion, Seat, Side } from '../api/types'
import { AnnounceBanner } from './AnnounceBanner'
import { EventLog } from './EventLog'
import { announcementAt, BANNER_SECONDS, timelineMarkers } from './highlights'
import { MapView } from './MapView'
import { MatchReport } from './MatchReport'
import { buildTracks, poseAt, type Pose } from './movement'
import { scoreboard } from './scoreboard'
import type { SeriesHeader } from '../live/seriesHeader'
import { ScoreBand, TeamRoster } from './ScoreBand'
import { Scrubber } from './Scrubber'
import { endTime, GAME_SECONDS_PER_SECOND, roster, stateAt } from './timeline'
import type { MatchEvent, Replay } from './types'

/** Haritanın altındaki şeritte görünen son olay sayısı (§8.39). */
const TICKER_SIZE = 3

interface Props {
  replay: Replay
  champions: Map<string, Champion>
  /** Oyuncunun koltuğu (lobide); paylaşım sayfasında yok. */
  you?: Seat
  /** Oynatma sona ulaşınca bir kez (izlendi → skor açılır, §8.15). */
  onFinished?: () => void
  /** Seri bilgisi (adlar, seri skoru, bağlantı; lobide). Paylaşım sayfasında yok, renk adları yazılır. */
  header?: SeriesHeader
  /** Lobide oynatıcıyı kapatır (zaman çizelgesinin sağında). */
  onClose?: () => void
}

// Maç oynatıcısı (DESIGN.md §7, §8.19): tek değişken oyun saati T; her karede T ilerler, ekran stateAt(T)'den çizilir.
// Duraklat / hız / sona atla yok (kullanıcı, 2026-11-24): maç hep 1x akar, bitince durur; zaman çizelgesinden
// geri sarılınca kendiliğinden yeniden akar. Atlamak isteyen çizelgeyi kullanır.
export function MatchPlayer({ replay, champions, you, onFinished, header, onClose }: Props) {
  const end = useMemo(() => endTime(replay), [replay])
  const players = useMemo(() => roster(replay), [replay])
  const [t, setT] = useState(0)
  const [showMath, setShowMath] = useState(false)
  const finishedRef = useRef(false)

  const state = useMemo(() => stateAt(replay, t), [replay, t])

  // Oynatma döngüsü: gerçek geçen süre × 20 oyun sn; maç bitmediyse döner. Sekme arkadayken tarayıcı kareyi
  // durdurur; dönünce büyük sıçrama olmasın diye kare başına en fazla 0,1 sn sayılır.
  useEffect(() => {
    if (state.finished) return
    let last: number | null = null
    let frame = requestAnimationFrame(function tick(now) {
      const dt = last === null ? 0 : Math.min(0.1, (now - last) / 1000)
      last = now
      setT((current) => Math.min(end, current + dt * GAME_SECONDS_PER_SECOND))
      frame = requestAnimationFrame(tick)
    })
    return () => cancelAnimationFrame(frame)
  }, [state.finished, end])

  useEffect(() => {
    if (state.finished && !finishedRef.current) {
      finishedRef.current = true
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

  // Büyük an bandı: son ~2 gerçek saniyenin (kaç oyun saniyesiyse) en yeni büyük anı (highlights.ts).
  const announcement = useMemo(
    () => announcementAt(replay.events, t, BANNER_SECONDS * GAME_SECONDS_PER_SECOND),
    [replay, t],
  )
  const markers = useMemo(() => timelineMarkers(replay.events, end), [replay, end])
  // Son olaylar şeridi (§8.39): log yeniden eskiye; şeritte eskiden yeniye (en yenisi altta, kayarak gelir).
  const ticker = useMemo(() => state.log.slice(0, TICKER_SIZE).reverse(), [state.log])

  return (
    <div className="player">
      <ScoreBand board={board} t={t} you={youSide} header={header} />

      {/* Izgara: kadrolar haritanın tam boyunda (üst ve alt kenar hizalı); son olaylar haritanın altında. */}
      <div className="player-main">
        <TeamRoster lines={board.champions.filter((c) => c.side === 'blue')} side="blue" champions={champions} />
        <div className="map-stage">
          <MapView state={state} events={replay.events} placements={placements} champions={champions} />
          {announcement && <AnnounceBanner key={announcement.index} announcement={announcement} />}
        </div>
        <TeamRoster lines={board.champions.filter((c) => c.side === 'red')} side="red" champions={champions} />
        <div className="ticker" aria-live="polite">
          <EventLog log={ticker} order={order} champions={champions} showMath={false} />
        </div>
      </div>

      <div className="timeline-row">
        <Scrubber t={t} end={end} markers={markers} onSeek={setT} />
        {onClose && (
          <button className="close" onClick={onClose}>
            Kapat
          </button>
        )}
      </div>

      {state.finished && <MatchReport replay={replay} champions={champions} you={you} />}

      <section className="full-log" aria-label="Maçın tüm olayları">
        <label className="check small">
          <input type="checkbox" checked={showMath} onChange={(e) => setShowMath(e.target.checked)} />
          Matematiği göster
        </label>
        <EventLog log={state.log} order={order} champions={champions} showMath={showMath} />
      </section>
    </div>
  )
}
