import type { Champion, Side } from '../api/types'
import { BASE, LANE_PATH, PIT, RIVER_SPOT, TOWER_S, pathPoint } from './geometry'
import type { Placement } from './positions'
import type { MatchState } from './timeline'
import type { Lane, MatchEvent } from './types'

const LANES: Lane[] = ['top', 'mid', 'bot']
const SIDES: Side[] = ['blue', 'red']

/** Olay işaretinin haritada kaldığı süre (oyun saniyesi). */
const POP_SECONDS = 25

interface Props {
  state: MatchState
  events: readonly MatchEvent[]
  placements: Map<string, { side: Side; placement: Placement }>
  champions: Map<string, Champion>
}

// SVG harita (DESIGN.md §7). Şampiyon konumları CSS geçişiyle yumuşar (transform), kare kare elle aradeğer yok.
export function MapView({ state, events, placements, champions }: Props) {
  return (
    <svg className="map" viewBox="0 0 600 600" role="img" aria-label="Maç haritası">
      <rect width="600" height="600" className="map-bg" />
      {/* Nehir: sol üstten sağ alta, çukurlardan geçer (mid'e dik). */}
      <path d="M0 0 L600 600" className="map-river" />
      {LANES.map((lane) => (
        <polyline key={lane} points={LANE_PATH[lane].map((p) => p.join(',')).join(' ')} className="map-lane" />
      ))}
      <circle cx={PIT.dragon[0]} cy={PIT.dragon[1]} r="22" className="map-pit" />
      <text x={PIT.dragon[0]} y={PIT.dragon[1] + 38} className="map-label">
        Ejder
      </text>
      <circle cx={PIT.baron[0]} cy={PIT.baron[1]} r="22" className="map-pit" />
      <text x={PIT.baron[0]} y={PIT.baron[1] - 30} className="map-label">
        Baron / Herald
      </text>

      {SIDES.map((side) => (
        <g key={side}>
          <rect x={BASE[side][0] - 26} y={BASE[side][1] - 26} width="52" height="52" rx="10" className={`map-base ${side}`} />
          {LANES.map((lane) =>
            TOWER_S[side].map((s, tier) => {
              const [x, y] = pathPoint(lane, s)
              const down = state.towers.lost[side][lane] > tier
              return (
                <rect
                  key={`${lane}${tier}`}
                  x={x - 6}
                  y={y - 6}
                  width="12"
                  height="12"
                  rx="2"
                  className={`map-tower ${side}${down ? ' down' : ''}`}
                />
              )
            }),
          )}
        </g>
      ))}

      {[...placements].map(([id, { side, placement }]) => (
        <g
          key={id}
          className={`map-champ${placement.dead ? ' dead' : ''}`}
          style={{ transform: `translate(${placement.point[0]}px, ${placement.point[1]}px)` }}
        >
          <clipPath id={`clip-${id}`}>
            <circle r="13" />
          </clipPath>
          <image href={`/champions/${id}.png`} x="-13" y="-13" width="26" height="26" clipPath={`url(#clip-${id})`}>
            <title>{champions.get(id)?.name ?? id}</title>
          </image>
          <circle r="13" className={`map-ring ${side}`} />
        </g>
      ))}

      <Pops state={state} events={events} placements={placements} />
    </svg>
  )
}

// Olay işaretleri: T'den türetilir (zamanlayıcı yok), geri sarınca da doğru.
function Pops({ state, events, placements }: Pick<Props, 'state' | 'events' | 'placements'>) {
  const recent = events.filter((e) => e.t <= state.t && state.t - e.t < POP_SECONDS)
  return (
    <g>
      {recent.map((e, i) => {
        const pop = popOf(e, placements)
        if (!pop) return null
        return (
          <text key={i} x={pop.x} y={pop.y - 18} className={`map-pop ${pop.side}`}>
            {pop.text}
          </text>
        )
      })}
    </g>
  )
}

function popOf(e: MatchEvent, placements: Props['placements']) {
  switch (e.type) {
    case 'kill': {
      // Kurban o anda ölü sayılıp üsse taşınır; işaret kavganın olduğu yerde, yani öldürenin yanında.
      const p = placements.get(e.killer)?.placement.point
      return p ? { x: p[0] + 10, y: p[1], text: '✕', side: e.side } : null
    }
    case 'objective': {
      const names = { dragon: 'Ejder', herald: 'Herald', baron: 'Baron', elder: 'Elder', finalFight: 'Takım savaşı' }
      return { x: PIT[e.objective][0], y: PIT[e.objective][1] - 16, text: names[e.objective], side: e.side }
    }
    case 'skirmish':
      return { x: RIVER_SPOT[e.river][0], y: RIVER_SPOT[e.river][1] - 10, text: 'Çatışma', side: e.side }
    case 'throw':
      return { x: 300, y: 300, text: 'Throw!', side: e.side }
    case 'dragonSoul':
      return { x: PIT.dragon[0], y: PIT.dragon[1] + 60, text: 'Ejder ruhu', side: e.side }
    default:
      return null
  }
}
