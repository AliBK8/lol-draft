import type { Marker } from './highlights'
import { clock } from './timeline'

interface Props {
  t: number
  end: number
  markers: readonly Marker[]
  onSeek: (t: number) => void
}

/**
 * Zaman çizelgesi (§8.29): maçın tamamı tek çubukta; üstünde kill (takım renginde çizgi), canavar, kule ve nexus
 * işaretleri. Altında gerçek bir kaydırıcı (`input range`): fareyle, dokunmayla ve klavyeyle o ana atlanır. Konum,
 * skor ve log T'den türetildiği için atlamak bedava.
 */
export function Scrubber({ t, end, markers, onSeek }: Props) {
  const progress = Math.min(1, t / end) * 100
  return (
    <div className="scrubber">
      <span className="scrubber-time">{clock(t)}</span>
      <div className="scrubber-track">
        <div className="scrubber-fill" style={{ width: `${progress}%` }} />
        <div className="scrubber-marks" aria-hidden="true">
          {markers.map((m, i) =>
            m.kind === 'kill' ? (
              <span key={i} className={`mark-kill ${m.side ?? ''}`} style={{ left: `${m.at * 100}%` }} />
            ) : (
              <img
                key={i}
                className={`mark-icon ${m.side ?? ''}`}
                src={`/map/${m.icon}.png`}
                alt=""
                style={{ left: `${m.at * 100}%` }}
              />
            ),
          )}
        </div>
        <input
          type="range"
          min={0}
          max={end}
          step={1}
          value={Math.round(t)}
          onChange={(e) => onSeek(Number(e.target.value))}
          aria-label="Maçta zaman"
          aria-valuetext={clock(t)}
        />
      </div>
      <span className="scrubber-time muted">{clock(end)}</span>
    </div>
  )
}
