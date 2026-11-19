import { useMemo } from 'react'
import type { Champion, Seat, Side } from '../api/types'
import { GoldChart } from './GoldChart'
import { scoreboard, shortGold } from './scoreboard'
import { endTime, stateAt } from './timeline'
import type { Replay, RuleLine, TeamGold } from './types'

const SIDE_TR: Record<Side, string> = { blue: 'Mavi', red: 'Kırmızı' }

const GOLD_ROWS: [keyof TeamGold, string][] = [
  ['passive', 'Pasif gelir'],
  ['farm', 'Farm (CS)'],
  ['supportItem', 'Destek eşyası'],
  ['kill', 'Kill'],
  ['bounty', 'Shutdown ödülü'],
  ['assist', 'Asist'],
  ['plate', 'Plaka'],
  ['tower', 'Kule'],
  ['objective', 'Objektif'],
]

// Maç sonu karnesi (DESIGN.md §7, §16.11; görünüm §8.30): "neden kazandın?". Matematik maç boyunca gizli, burada
// açılır. Üstte kazanan bandı ve maç sonu özeti, altında altın farkı grafiği, altın kaynakları (karşılıklı
// çubuklar), draft kuralları, hiper taşıyıcılar, rol dışı kumarlar.
export function MatchReport({ replay, champions, you }: { replay: Replay; champions: Map<string, Champion>; you?: Seat }) {
  const { end, report } = replay
  const youSide: Side | null = you ? (replay.blueTeam === you ? 'blue' : 'red') : null
  const name = (id: string) => champions.get(id)?.name ?? id
  const final = useMemo(() => scoreboard(replay, stateAt(replay, endTime(replay))), [replay])

  return (
    <div className="report">
      <header className={`report-hero ${end.winner}`}>
        <h2>{youSide ? (youSide === end.winner ? 'Kazandın!' : 'Kaybettin.') : `${SIDE_TR[end.winner]} kazandı`}</h2>
        <p className="muted">
          {end.minute}. dakika · {end.kind === 'siege' ? 'kuşatmayla' : 'son savaşla'} bitti · bu savaşı kazanma ihtimali %
          {Math.round(end.winnerProbability * 100)}
        </p>
        <dl className="final-stats">
          {(
            [
              ['Kill', final.teams.blue.kills, final.teams.red.kills],
              ['Altın', shortGold(report.blue.total), shortGold(report.red.total)],
              ['Kule', final.teams.blue.towers, final.teams.red.towers],
              ['Ejder', final.teams.blue.objectives.dragon, final.teams.red.objectives.dragon],
              ['Baron', final.teams.blue.objectives.baron, final.teams.red.objectives.baron],
            ] as const
          ).map(([label, blue, red]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd>
                <span className="blue">{blue}</span> – <span className="red">{red}</span>
              </dd>
            </div>
          ))}
        </dl>
      </header>

      <GoldChart replay={replay} />

      <div className="report-grid">
        <section className="report-card">
          <h3>Altın kaynakları</h3>
          <GoldSources blue={report.blue} red={report.red} />
        </section>

        <section className="report-card">
          <h3>Draft kuralları</h3>
          <Rules rules={report.rules} />
        </section>
      </div>

      {(report.carries.length > 0 || report.offRoles.length > 0) && (
        <div className="report-grid">
          {report.carries.length > 0 && (
            <section className="report-card">
              <h3>Hiper taşıyıcılar</h3>
              <ul className="report-list">
                {report.carries.map((c) => (
                  <li key={`${c.side}${c.champion}`} className={c.side}>
                    <img src={`/champions/${c.champion}.png`} alt="" width={32} height={32} />
                    <span>{c.text}</span>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {report.offRoles.length > 0 && (
            <section className="report-card">
              <h3>Rol dışı kumarlar</h3>
              <ul className="report-list">
                {report.offRoles.map((o) => (
                  <li key={`${o.side}${o.champion}`} className={o.side}>
                    <img src={`/champions/${o.champion}.png`} alt="" width={32} height={32} />
                    <span>
                      {o.text}
                      <span className="muted small">
                        {' '}
                        ({name(o.champion)}, ceza −%{Math.round(o.penalty * 100)}, rol rakibine göre{' '}
                        {o.goldDiff >= 0 ? '+' : ''}
                        {o.goldDiff.toLocaleString('tr-TR')} altın)
                      </span>
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </div>
      )}
    </div>
  )
}

/**
 * Altın kaynakları: her kalem bir satır, mavi sola kırmızı sağa çubuk. Ölçek bütün kalemlerde ortak (en büyük
 * kalem dolu çubuk): kalemler birbiriyle de karşılaştırılır. Değerler yazıyla, renk sadece çubukta.
 */
function GoldSources({ blue, red }: { blue: TeamGold; red: TeamGold }) {
  const max = Math.max(1, ...GOLD_ROWS.flatMap(([key]) => [blue[key], red[key]]))
  return (
    <div className="versus">
      <div className="versus-row total">
        <span className="versus-value">{blue.total.toLocaleString('tr-TR')}</span>
        <span className="versus-half" />
        <span className="versus-label">Toplam</span>
        <span className="versus-half" />
        <span className="versus-value">{red.total.toLocaleString('tr-TR')}</span>
      </div>
      {GOLD_ROWS.map(([key, label]) => (
        <div key={key} className="versus-row">
          <span className="versus-value">{blue[key].toLocaleString('tr-TR')}</span>
          <span className="versus-half blue">
            <span className="versus-bar blue" style={{ width: `${(blue[key] / max) * 100}%` }} />
          </span>
          <span className="versus-label">{label}</span>
          <span className="versus-half red">
            <span className="versus-bar red" style={{ width: `${(red[key] / max) * 100}%` }} />
          </span>
          <span className="versus-value">{red[key].toLocaleString('tr-TR')}</span>
        </div>
      ))}
    </div>
  )
}

// Kural satırları: etkiye göre, iki takım ayrı; çubuk en büyük etkiye oranla.
function Rules({ rules }: { rules: RuleLine[] }) {
  if (rules.length === 0) return <p className="muted">Bu maçta öne çıkan kural yok.</p>
  const max = Math.max(...rules.map((r) => Math.abs(r.impact)), 1)
  const sorted = [...rules].sort((a, b) => Math.abs(b.impact) - Math.abs(a.impact)).slice(0, 12)
  return (
    <ul className="rules">
      {sorted.map((r, i) => (
        <li key={i} className={r.side}>
          <span className="rule-bar" style={{ width: `${(Math.abs(r.impact) / max) * 100}%` }} />
          <span>
            {r.text} <span className="muted small">×{r.count}</span>
          </span>
        </li>
      ))}
    </ul>
  )
}
