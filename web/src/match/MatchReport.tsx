import type { Champion, Seat, Side } from '../api/types'
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
  ['total', 'Toplam'],
]

// Maç sonu karnesi (DESIGN.md §7, §16.11): "neden kazandın?". Matematik maç boyunca gizli, burada açılır.
export function MatchReport({ replay, champions, you }: { replay: Replay; champions: Map<string, Champion>; you?: Seat }) {
  const { end, report } = replay
  const youSide: Side | null = you ? (replay.blueTeam === you ? 'blue' : 'red') : null
  const name = (id: string) => champions.get(id)?.name ?? id

  return (
    <div className="report">
      <h2 className={end.winner}>
        {youSide ? (youSide === end.winner ? 'Kazandın!' : 'Kaybettin.') : `${SIDE_TR[end.winner]} kazandı`}
        <span className="muted small">
          {' '}
          · {end.minute}. dakika · {end.kind === 'siege' ? 'kuşatmayla' : 'son savaşla'} bitti · bu savaşı kazanma
          ihtimali %{Math.round(end.winnerProbability * 100)}
        </span>
      </h2>

      <div className="report-grid">
        <section>
          <h3>Altın kaynakları</h3>
          <table className="gold-table">
            <thead>
              <tr>
                <th />
                <th className="blue">Mavi</th>
                <th className="red">Kırmızı</th>
              </tr>
            </thead>
            <tbody>
              {GOLD_ROWS.map(([key, label]) => (
                <tr key={key} className={key === 'total' ? 'total' : ''}>
                  <td>{label}</td>
                  <td>{report.blue[key].toLocaleString('tr-TR')}</td>
                  <td>{report.red[key].toLocaleString('tr-TR')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>

        <section>
          <h3>Draft kuralları</h3>
          <Rules rules={report.rules} />
        </section>
      </div>

      {report.carries.length > 0 && (
        <section>
          <h3>Hiper taşıyıcılar</h3>
          <ul>
            {report.carries.map((c) => (
              <li key={`${c.side}${c.champion}`} className={c.side}>
                {c.text}
              </li>
            ))}
          </ul>
        </section>
      )}

      {report.offRoles.length > 0 && (
        <section>
          <h3>Rol dışı kumarlar</h3>
          <ul>
            {report.offRoles.map((o) => (
              <li key={`${o.side}${o.champion}`} className={o.side}>
                {o.text}
                <span className="muted small">
                  {' '}
                  ({name(o.champion)}, ceza −%{Math.round(o.penalty * 100)}, rol rakibine göre{' '}
                  {o.goldDiff >= 0 ? '+' : ''}
                  {o.goldDiff.toLocaleString('tr-TR')} altın)
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}
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
