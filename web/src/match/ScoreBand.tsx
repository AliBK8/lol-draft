import type { Champion, Side } from '../api/types'
import { clock } from './timeline'
import { shortGold, type ChampionLine, type Scoreboard, type TeamLine } from './scoreboard'

const SIDE_TR: Record<Side, string> = { blue: 'Mavi', red: 'Kırmızı' }
/** Altın çubuğu bu farkta dolar (yarım çubuk = 10.000 altın önde). */
const GOLD_BAR_FULL = 10000

/** Takımın yıktığı kule ve aldığı canavarlar (oyunun minimap ikonları); Herald ve Elder sadece alındıysa. */
function TeamStats({ team, side }: { team: TeamLine; side: Side }) {
  const items: [string, string, number][] = [
    ['tower', 'Kule', team.towers],
    ['dragon', 'Ejder', team.objectives.dragon],
    ['riftherald', 'Herald', team.objectives.herald],
    ['baron', 'Baron', team.objectives.baron],
    ['dragon_elder', 'Elder', team.objectives.elder],
  ]
  const shown = items.filter(([icon, , n]) => n > 0 || icon === 'tower' || icon === 'dragon' || icon === 'baron')
  return (
    <span className={`team-stats ${side}`}>
      {shown.map(([icon, label, n]) => (
        <span key={icon} className="team-stat" title={`${label}: ${n}`}>
          <img src={`/map/${icon}.png`} alt={label} width={20} height={20} />
          {n}
        </span>
      ))}
    </span>
  )
}

/** Yayın skor bandı: kill skoru ve saat, altında iki takımın kule/canavar sayısı ve önde olana doğru büyüyen altın çubuğu. */
export function ScoreBand({ board, t, you }: { board: Scoreboard; t: number; you: Side | null }) {
  const { blue, red } = board.teams
  const diff = blue.gold - red.gold
  const fill = Math.min(1, Math.abs(diff) / GOLD_BAR_FULL) * 50
  const rounded = Math.round(Math.abs(diff) / 100) * 100
  return (
    <div className="score-band">
      <span className="score-team blue">
        {SIDE_TR.blue}
        {you === 'blue' && <small> (sen)</small>}
      </span>
      <span className="score-kills blue">{blue.kills}</span>
      <span className="score-clock">{clock(t)}</span>
      <span className="score-kills red">{red.kills}</span>
      <span className="score-team red">
        {you === 'red' && <small>(sen) </small>}
        {SIDE_TR.red}
      </span>

      <TeamStats team={blue} side="blue" />
      <div className="gold-bar" title="Altın farkı">
        <span className={`gold-fill ${diff >= 0 ? 'blue' : 'red'}`} style={{ width: `${fill}%` }} />
        <span className="gold-label">
          {rounded === 0 ? 'Altın eşit' : `${diff > 0 ? 'Mavi' : 'Kırmızı'} +${rounded.toLocaleString('tr-TR')}`}
        </span>
      </div>
      <TeamStats team={red} side="red" />
    </div>
  )
}

/** Takım kadrosu: portre (ölüyse gri + doğma sayacı), ad, kill/ölüm, altın. Kırmızı ayna. */
export function TeamRoster({ lines, side, champions }: { lines: ChampionLine[]; side: Side; champions: Map<string, Champion> }) {
  return (
    <ol className={`roster ${side}`} aria-label={`${SIDE_TR[side]} kadrosu`}>
      {lines.map((c) => (
        <li key={c.id} className={c.respawnIn !== null ? 'dead' : ''}>
          <span className="roster-portrait">
            <img src={`/champions/${c.id}.png`} alt="" width={40} height={40} />
            {c.respawnIn !== null && <span className="respawn">{c.respawnIn}</span>}
          </span>
          <span className="roster-info">
            <span className="roster-name">
              <img src={`/roles/${c.role}.png`} alt="" width={14} height={14} />
              <span className="roster-label">{champions.get(c.id)?.name ?? c.id}</span>
            </span>
            <span className="roster-stats">
              <b>
                {c.kills}/{c.deaths}
              </b>
              <span className="muted">{shortGold(c.gold)}</span>
            </span>
          </span>
        </li>
      ))}
    </ol>
  )
}
