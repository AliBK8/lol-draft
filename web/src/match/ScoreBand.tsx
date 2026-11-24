import type { Champion, Side } from '../api/types'
import { CoinIcon } from '../components/CoinIcon'
import { SeriesChips, SeriesPips } from '../components/SeriesHeader'
import { SIDE_TR, teamLabel, type SeriesHeader } from '../live/seriesHeader'
import { clock } from './timeline'
import { shortGold, type ChampionLine, type Scoreboard, type TeamLine } from './scoreboard'

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

/**
 * Yayın skor tabelası (§8.38): tek kutu değil, birbirine geçen üç parça. İki takım plakasının iç kenarı çapraz
 * kesik, ortadaki saat parçası ters yamuk; kesikler paralel, aralarında hep aynı ince boşluk kalır.
 * Plaka: takım adı + seri noktaları + "Sen", koparsa bağlantı uyarısı, kule/canavar sayıları; kill sayısı iç uçta,
 * ortaya yakın, önde olanın altında altın farkı. Orta parça: saat, önde olana doğru büyüyen altın çubuğu, BO/Fearless.
 * Paylaşım sayfasında header yok: renk adları yazılır, noktalar ve bağlantı yok.
 */
export function ScoreBand(props: { board: Scoreboard; t: number; you: Side | null; header?: SeriesHeader }) {
  const { board, t, you, header } = props
  const diff = board.teams.blue.gold - board.teams.red.gold
  const fill = Math.min(1, Math.abs(diff) / GOLD_BAR_FULL) * 50
  const rounded = Math.round(Math.abs(diff) / 100) * 100
  const label = (side: Side) => teamLabel(side, header)
  // Altın farkı önde olanın plakasında, kill sayısının altında ("+3,4k"); eşitse (yüzlere yuvarlanmış 0) yok.
  const leader: Side | null = rounded === 0 ? null : diff > 0 ? 'blue' : 'red'
  const plate = (side: Side) => (
    <div className={`hud-team ${side}`}>
      <span className="hud-id">
        <span className="hud-name">
          <span className="hud-label">{label(side)}</span>
          <SeriesPips side={side} header={header} />
          {you === side && <span className="you-tag">Sen</span>}
        </span>
        {/* Bağlantı sadece koparsa görünür (SeatPresence quiet); bağlıyken satır boş kalır, yer kaplamaz. */}
        {header?.teams[side].presence}
      </span>
      <TeamStats team={board.teams[side]} side={side} />
      <span className="hud-score">
        <span className="hud-score-label" aria-hidden="true">
          Kill
        </span>
        <span className="hud-kills" aria-label={`${label(side)} kill`}>
          {board.teams[side].kills}
        </span>
        {/* Satır iki tarafta da var (gerideyse görünmez): iki kill sayısı aynı yükseklikte kalsın. */}
        {leader === side ? (
          <span className="hud-gold gold-amount" title={`${rounded.toLocaleString('tr-TR')} altın önde`}>
            +<CoinIcon />
            {shortGold(rounded)}
          </span>
        ) : (
          <span className="hud-gold gold-amount placeholder" aria-hidden="true">
            +<CoinIcon />0
          </span>
        )}
      </span>
    </div>
  )
  return (
    <div className="hud">
      {plate('blue')}
      <div className="hud-center">
        <span className="hud-clock">{clock(t)}</span>
        <div className="gold-bar" title={leader ? `${label(leader)} ${rounded.toLocaleString('tr-TR')} altın önde` : 'Altın eşit'}>
          <span className={`gold-fill ${diff >= 0 ? 'blue' : 'red'}`} style={{ width: `${fill}%` }} />
        </div>
        <SeriesChips header={header} />
      </div>
      {plate('red')}
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
              <b className="roster-kd" title="Kill / ölüm">
                {c.kills}/{c.deaths}
              </b>
              <span className="gold-amount muted" title={`${c.gold.toLocaleString('tr-TR')} altın`}>
                <CoinIcon />
                {shortGold(c.gold)}
              </span>
            </span>
          </span>
        </li>
      ))}
    </ol>
  )
}
