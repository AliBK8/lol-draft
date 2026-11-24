import type { Champion } from '../api/types'
import { clock } from './timeline'
import { winnerSide, type MatchEvent } from './types'

/** Takım rengiyle vurgulanan büyük olaylar. */
const BIG = new Set(['objective', 'nexus', 'tower', 'throw', 'skirmish', 'dragonSoul', 'invade'])

const OBJECTIVE_ICON = { dragon: 'dragon', elder: 'dragon_elder', herald: 'riftherald', baron: 'baron' } as const

/** Olayın ikonu: kill'de iki portre (öldüren ⚔ ölen), canavarlarda oyunun ikonu, diğerlerinde küçük işaret. */
function EventIcon({ e, champions }: { e: MatchEvent; champions: Map<string, Champion> }) {
  const portrait = (id: string) => (
    <img className="log-portrait" src={`/champions/${id}.png`} alt={champions.get(id)?.name ?? id} width={24} height={24} />
  )
  const icon = (name: string, label: string) => <img className="log-icon" src={`/map/${name}.png`} alt={label} width={22} height={22} />
  switch (e.type) {
    case 'kill':
      return (
        <span className="log-kill">
          {portrait(e.killer)}
          <span className="log-swords">⚔</span>
          {portrait(e.victim)}
        </span>
      )
    case 'objective':
      return e.objective === 'finalFight' ? <span className="log-glyph">⚔</span> : icon(OBJECTIVE_ICON[e.objective], e.objective)
    case 'dragonSoul':
      return icon('dragon', 'ejder ruhu')
    case 'tower':
    case 'plate':
      return icon('tower', 'kule')
    case 'nexus':
      return icon('nexus', 'nexus')
    case 'skirmish':
      return <span className="log-glyph">⚔</span>
    case 'invade':
      // İstilacı ormancının portresi.
      return portrait(e.invaders[0])
    case 'throw':
      return <span className="log-glyph">!</span>
    default:
      return <span className="log-glyph small">•</span>
  }
}

interface Props {
  /** Log'a düşmüş olaylar, yeniden eskiye (MatchState.log). */
  log: readonly MatchEvent[]
  /** Olay → belgedeki sırası: kart anahtarı sabit kalsın (yeni kart kayarak gelir, eskiler yeniden çizilmez). */
  order: Map<MatchEvent, number>
  champions: Map<string, Champion>
  showMath: boolean
}

// Maç log'u (§8.28): her olay bir kart; saat, ikon (kill'de portreler), metin. Büyük olaylar takım renginde.
export function EventLog({ log, order, champions, showMath }: Props) {
  return (
    <ol className="log">
      {log.map((e) => {
        const side = winnerSide(e) ?? ''
        return (
          <li key={order.get(e)} className={`log-card ${side}${BIG.has(e.type) ? ' big' : ''}`}>
            <span className="log-time">{clock(e.t)}</span>
            <EventIcon e={e} champions={champions} />
            <span className="log-text">
              {e.text}
              {showMath && 'probability' in e && typeof e.probability === 'number' && (
                <span className="muted small"> %{Math.round(e.probability * 100)}</span>
              )}
            </span>
          </li>
        )
      })}
    </ol>
  )
}
