import { useId, useMemo, useState, type PointerEvent } from 'react'
import { clock } from './timeline'
import type { MatchEvent, Replay } from './types'

// Altın farkı grafiği (karne, §8.30): tek seri, iki yönlü (mavi önde yukarı, kırmızı önde aşağı). Çizgi ve hafif alan
// sıfırın üstünde mavi, altında kırmızı (aynı yol iki kez, kırpılarak). Üstte canavar anları oyun ikonlarıyla.
// Üzerine gelince en yakın dakikaya oturan dikey çizgi ve kutu; tablo görünümü açılır kutuda.

const W = 640
const H = 230
const LEFT = 46
const RIGHT = 14
const TOP = 34
const BOTTOM = 26
const PLOT_W = W - LEFT - RIGHT
const PLOT_H = H - TOP - BOTTOM

/** Eksenin üst sınırı: farkı kapsayan en küçük yuvarlak sayı (en az 1.000). */
function niceMax(value: number): number {
  for (const step of [1000, 2000, 2500, 5000, 7500, 10000, 15000, 20000, 25000, 30000, 40000, 50000]) {
    if (value <= step) return step
  }
  return Math.ceil(value / 10000) * 10000
}

/** Eksen etiketi kısa ("+5k", "−2,5k"): dar ekranda büyüyen yazı sol boşluğa sığsın; tam değer imleç kutusunda. */
const axisLabel = (v: number) =>
  v === 0 ? '0' : `${v > 0 ? '+' : '−'}${(Math.abs(v) / 1000).toLocaleString('tr-TR', { maximumFractionDigits: 1 })}k`

const ICON = { dragon: 'dragon', elder: 'dragon_elder', herald: 'riftherald', baron: 'baron' } as const
const NAME = { dragon: 'Ejder', elder: 'Elder', herald: 'Herald', baron: 'Baron', finalFight: 'Son savaş' } as const
const SIDE_TR = { blue: 'Mavi', red: 'Kırmızı' } as const

interface Moment {
  minute: number
  icon: string
  side: 'blue' | 'red'
  label: string
}

function moments(events: readonly MatchEvent[]): Moment[] {
  const list: Moment[] = []
  for (const e of events) {
    if (e.type === 'objective' && e.objective !== 'finalFight')
      list.push({ minute: e.t / 60, icon: ICON[e.objective], side: e.side, label: `${NAME[e.objective]} (${SIDE_TR[e.side]})` })
    else if (e.type === 'nexus') list.push({ minute: e.t / 60, icon: 'nexus', side: e.side, label: `Nexus (${SIDE_TR[e.side]})` })
  }
  return list
}

export function GoldChart({ replay }: { replay: Replay }) {
  const id = useId()
  const [hover, setHover] = useState<number | null>(null)

  // Dakika başına mavi − kırmızı toplam altın.
  const diffs = useMemo(
    () => replay.goldFrames.map((f) => f.champions.reduce((s, c) => s + (c.side === 'blue' ? c.gold : -c.gold), 0)),
    [replay],
  )
  const marks = useMemo(() => moments(replay.events), [replay])
  if (diffs.length < 2) return null

  const lastMinute = diffs.length - 1
  const max = niceMax(Math.max(...diffs.map(Math.abs)))
  const x = (minute: number) => LEFT + (Math.min(minute, lastMinute) / lastMinute) * PLOT_W
  const y = (gold: number) => TOP + PLOT_H / 2 - (gold / max) * (PLOT_H / 2)
  const zero = y(0)

  const line = diffs.map((d, m) => `${m === 0 ? 'M' : 'L'}${x(m).toFixed(1)},${y(d).toFixed(1)}`).join(' ')
  const area = `${line} L${x(lastMinute)},${zero} L${x(0)},${zero} Z`
  const xTicks = Array.from({ length: Math.floor(lastMinute / 5) + 1 }, (_, i) => i * 5)

  // İmleç: en yakın dakikaya oturur (okuyucu 2 piksellik çizgiye değil, bir dakikaya nişan alır).
  function onMove(event: PointerEvent<SVGRectElement>) {
    const box = event.currentTarget.ownerSVGElement!.getBoundingClientRect()
    const px = ((event.clientX - box.left) / box.width) * W
    setHover(Math.max(0, Math.min(lastMinute, Math.round(((px - LEFT) / PLOT_W) * lastMinute))))
  }

  const hovered = hover === null ? null : diffs[hover]
  const hoverMarks = hover === null ? [] : marks.filter((m) => Math.round(m.minute) === hover)

  return (
    <figure className="gold-chart">
      <figcaption>
        <strong>Altın farkı</strong>
        <span className="muted small"> · yukarı Mavi önde, aşağı Kırmızı önde · yatay eksen dakika</span>
      </figcaption>
      <div className="gold-chart-plot">
        <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="Maç boyunca altın farkı">
          <defs>
            <clipPath id={`${id}-up`}>
              <rect x={0} y={0} width={W} height={zero} />
            </clipPath>
            <clipPath id={`${id}-down`}>
              <rect x={0} y={zero} width={W} height={H - zero} />
            </clipPath>
          </defs>

          {/* Izgara: sadece ±üst sınır ve yarısı, saç teli. */}
          {[max, max / 2, -max / 2, -max].map((v) => (
            <line key={v} className="gc-grid" x1={LEFT} x2={W - RIGHT} y1={y(v)} y2={y(v)} />
          ))}
          {[max, 0, -max].map((v) => (
            <text key={v} className="gc-axis" x={LEFT - 6} y={y(v) + 4} textAnchor="end">
              {axisLabel(v)}
            </text>
          ))}
          {xTicks.map((m) => (
            <text key={m} className="gc-axis" x={x(m)} y={H - 8} textAnchor="middle">
              {m}
            </text>
          ))}

          <path d={area} className="gc-area blue" clipPath={`url(#${id}-up)`} />
          <path d={area} className="gc-area red" clipPath={`url(#${id}-down)`} />
          <line className="gc-zero" x1={LEFT} x2={W - RIGHT} y1={zero} y2={zero} />
          <path d={line} className="gc-line blue" clipPath={`url(#${id}-up)`} />
          <path d={line} className="gc-line red" clipPath={`url(#${id}-down)`} />

          {/* Canavar anları: üst şeritte ikon, altında çizgiye kadar ince işaret. */}
          {marks.map((m, i) => (
            <g key={i}>
              <line className={`gc-moment ${m.side}`} x1={x(m.minute)} x2={x(m.minute)} y1={TOP - 4} y2={TOP + PLOT_H} />
              <image href={`/map/${m.icon}.png`} x={x(m.minute) - 9} y={4} width={18} height={18}>
                <title>{m.label}</title>
              </image>
            </g>
          ))}

          {hover !== null && hovered !== null && (
            <g>
              <line className="gc-cross" x1={x(hover)} x2={x(hover)} y1={TOP} y2={TOP + PLOT_H} />
              <circle className={`gc-dot ${hovered >= 0 ? 'blue' : 'red'}`} cx={x(hover)} cy={y(hovered)} r={5} />
            </g>
          )}

          {/* Hedef: bütün çizim alanı (çizgiye değil dakikaya nişan). */}
          <rect
            className="gc-hit"
            x={LEFT}
            y={TOP}
            width={PLOT_W}
            height={PLOT_H}
            onPointerMove={onMove}
            onPointerDown={onMove}
            onPointerLeave={() => setHover(null)}
          />
        </svg>

        {hover !== null && hovered !== null && (
          <div className="gc-tip" style={{ left: `${(x(hover) / W) * 100}%`, top: `${(TOP / H) * 100}%` }}>
            <strong>{hovered === 0 ? 'Eşit' : `+${Math.abs(hovered).toLocaleString('tr-TR')}`}</strong>
            <span>
              {hovered === 0 ? '' : `${hovered > 0 ? 'Mavi' : 'Kırmızı'} önde · `}
              {clock(hover * 60)}
            </span>
            {hoverMarks.map((m, i) => (
              <span key={i}>{m.label}</span>
            ))}
          </div>
        )}
      </div>

      <details className="gc-table">
        <summary className="small muted">Tablo olarak göster</summary>
        <table className="gold-table">
          <thead>
            <tr>
              <th>Dakika</th>
              <th>Fark</th>
              <th>Önde</th>
            </tr>
          </thead>
          <tbody>
            {diffs.map((d, m) => (
              <tr key={m}>
                <td>{m}</td>
                <td>{Math.abs(d).toLocaleString('tr-TR')}</td>
                <td>{d === 0 ? '—' : d > 0 ? 'Mavi' : 'Kırmızı'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </figure>
  )
}
