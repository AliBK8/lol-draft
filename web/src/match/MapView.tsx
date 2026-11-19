import type { Champion, Side } from '../api/types'
import { BASE, INHIBITORS, MAP_IMAGE, MAP_SIZE, PIT, RIVER_SPOT, TOWERS } from './geometry'
import type { Pose } from './movement'
import type { MatchState } from './timeline'
import type { Lane, MatchEvent } from './types'

const LANES: Lane[] = ['top', 'mid', 'bot']
const SIDES: Side[] = ['blue', 'red']

// Kule, inhibitör ve nexus: oyunun gri minimap ikonları (CommunityDragon, npm run icons), takım rengine filtreyle
// boyanır. Boyutlar harita biriminde (şampiyon ikonu 26).
const TOWER_ICON = '/map/tower.png'
const INHIBITOR_ICON = '/map/inhibitor.png'
const NEXUS_ICON = '/map/nexus.png'
const TOWER_SIZE = 26
const INHIBITOR_SIZE = 22
const NEXUS_SIZE = 34

/** Merkezi verilen kare ikon (kule, inhibitör, nexus); yıkılabilenin sınıfı `kind` (+ ` down`). */
function Icon({ href, at, size, kind, down = false }: {
  href: string
  at: readonly [number, number]
  size: number
  kind?: string
  down?: boolean
}) {
  return (
    <image
      href={href}
      x={at[0] - size / 2}
      y={at[1] - size / 2}
      width={size}
      height={size}
      className={kind && `${kind}${down ? ' down' : ''}`}
    />
  )
}

// Renk matrisi: her kanal kendi çarpanıyla (gri × takım rengi). Siyah kenar siyah kalır, açık gri takım rengine döner.
// İkonun açık grisi ~0,75: çarpanlar 1'in üstünde ki renk karanlık kalmasın (mavi ≈ #4a90e2, kırmızı ≈ #e2564a).
const TINT: Record<Side, string> = {
  blue: '0.45 0 0 0 0  0 0.85 0 0 0  0 0 1.35 0 0  0 0 0 1 0',
  red: '1.35 0 0 0 0  0 0.5 0 0 0  0 0 0.45 0 0  0 0 0 1 0',
}

/** Olay işaretinin haritada kaldığı süre (oyun saniyesi). */
const POP_SECONDS = 25

interface Props {
  state: MatchState
  events: readonly MatchEvent[]
  placements: Map<string, { side: Side; placement: Pose }>
  champions: Map<string, Champion>
}

// SVG harita (DESIGN.md §7): Summoner's Rift görseli üstünde kuleler, ikonlar ve olay işaretleri.
// Şampiyon konumları her karede güzergâhtan gelir (movement.ts); CSS geçişi yok, yoksa ikon geride kalırdı.
export function MapView({ state, events, placements, champions }: Props) {
  return (
    <svg className="map" viewBox={`0 0 ${MAP_SIZE} ${MAP_SIZE}`} role="img" aria-label="Maç haritası">
      {/* Görsel yüklenemezse koyu zemin kalır; kuleler ve ikonlar yine görünür. */}
      <rect width={MAP_SIZE} height={MAP_SIZE} className="map-bg" />
      <image href={MAP_IMAGE} width={MAP_SIZE} height={MAP_SIZE} className="map-image" />

      <defs>
        {SIDES.map((side) => (
          <filter key={side} id={`tint-${side}`} colorInterpolationFilters="sRGB">
            <feColorMatrix type="matrix" values={TINT[side]} />
          </filter>
        ))}
      </defs>

      {SIDES.map((side) => (
        <g key={side} filter={`url(#tint-${side})`}>
          <Icon href={NEXUS_ICON} at={BASE[side]} size={NEXUS_SIZE} />
          {LANES.map((lane) => (
            <g key={lane}>
              {/* Üçüncü kademe (üs kulesi + inhibitör) motorda tek yapı: düşünce ikisi birlikte yıkık. */}
              <Icon
                href={INHIBITOR_ICON}
                at={INHIBITORS[side][lane]}
                size={INHIBITOR_SIZE}
                kind="map-inhibitor"
                down={state.towers.lost[side][lane] > 2}
              />
              {TOWERS[side][lane].map((point, tier) => (
                <Icon
                  key={tier}
                  href={TOWER_ICON}
                  at={point}
                  size={TOWER_SIZE}
                  kind="map-tower"
                  down={state.towers.lost[side][lane] > tier}
                />
              ))}
            </g>
          ))}
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
      // İşaret kavganın olduğu yerde, öldürenin yanında.
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
