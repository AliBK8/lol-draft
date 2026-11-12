import { useMemo, useState } from 'react'
import type { Champion, DraftState, Role, Side } from '../api/types'
import { ROLES } from '../api/types'
import { ChampionIcon } from '../components/ChampionIcon'
import { Countdown } from '../components/Countdown'
import { bans, DRAFT_ORDER, isMyTurn, lineup, ROLE_TR, unavailable } from '../live/draft'
import type { ActionResult, HubMethod } from '../live/useLobbyConnection'

const PHASE_TR: Record<string, string> = { ban1: 'Ban 1', pick1: 'Pick 1', ban2: 'Ban 2', pick2: 'Pick 2' }
const SIDE_TR: Record<Side, string> = { blue: 'Mavi', red: 'Kırmızı' }

interface Props {
  draft: DraftState
  me: Side
  champions: Map<string, Champion>
  offsetMs: number
  act: (method: HubMethod, ...args: string[]) => Promise<ActionResult>
}

// Ban/pick (DESIGN.md §2): sıra sendeyse havuzdan seç, onayla. Kurallar sunucuda; burada tıklanamayanlar sadece görüntü.
export function DraftBoard({ draft, me, champions, offsetMs, act }: Props) {
  const [role, setRole] = useState<Role | 'all'>('all')
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const myTurn = isMyTurn(draft, me)
  const taken = useMemo(() => unavailable(draft), [draft])
  const pool = useMemo(() => {
    const needle = search.trim().toLocaleLowerCase('tr')
    return [...champions.values()]
      .filter((c) => role === 'all' || c.roles.includes(role))
      .filter((c) => !needle || c.name.toLocaleLowerCase('tr').includes(needle))
      .sort((a, b) => a.name.localeCompare(b.name, 'tr'))
  }, [champions, role, search])

  // Seçim sırası değişince (rakip oynadı, süre doldu) eski seçim düşer.
  const choice = selected && !taken.has(selected) ? selected : null

  async function confirm() {
    if (!choice || !draft.step) return
    setBusy(true)
    const result = await act(draft.step === 'ban' ? 'Ban' : 'Pick', choice)
    setBusy(false)
    if (result.ok) {
      setSelected(null)
      setError(null)
    } else {
      setError(result.message)
    }
  }

  const next = DRAFT_ORDER[draft.actions.length]
  return (
    <div className="draft">
      <div className="draft-bar">
        <span>{PHASE_TR[draft.phase] ?? draft.phase}</span>
        <span className={`turn ${draft.turn ?? ''}`}>
          {myTurn ? 'Sıra sende' : `Sıra ${draft.turn ? SIDE_TR[draft.turn] : ''}`}: {next?.kind === 'ban' ? 'ban' : 'pick'}
        </span>
        <Countdown deadline={draft.deadline} offsetMs={offsetMs} />
        <span className="muted small">Adım {draft.actions.length + 1}/20 · Sen {SIDE_TR[me]}sin</span>
      </div>

      <div className="draft-main">
        <TeamColumn side="blue" draft={draft} champions={champions} mine={me === 'blue'} />

        <div className="pool">
          <div className="pool-filters">
            <button className={role === 'all' ? 'on' : ''} onClick={() => setRole('all')}>
              Hepsi
            </button>
            {ROLES.map((r) => (
              <button key={r} className={role === r ? 'on' : ''} onClick={() => setRole(r)}>
                {ROLE_TR[r]}
              </button>
            ))}
            <input placeholder="Ara" value={search} onChange={(e) => setSearch(e.target.value)} />
          </div>
          <div className="pool-grid">
            {pool.map((c) => {
              const out = taken.has(c.id)
              const locked = draft.fearlessLocks.includes(c.id)
              return (
                <button
                  key={c.id}
                  className={['pool-item', choice === c.id ? 'picked' : ''].join(' ')}
                  disabled={out || !myTurn}
                  onClick={() => setSelected(c.id)}
                  title={locked ? `${c.name} (fearless kilidi)` : c.name}
                >
                  <ChampionIcon id={c.id} name={c.name} size={56} dim={out} />
                  <span>{c.name}</span>
                </button>
              )
            })}
          </div>
          <div className="pool-confirm">
            <button className="primary" disabled={!myTurn || !choice || busy} onClick={confirm}>
              {draft.step === 'ban' ? 'Banla' : 'Seç'}
              {choice ? `: ${champions.get(choice)?.name}` : ''}
            </button>
            {error && <span className="error">{error}</span>}
          </div>
        </div>

        <TeamColumn side="red" draft={draft} champions={champions} mine={me === 'red'} />
      </div>
    </div>
  )
}

function TeamColumn(props: { side: Side; draft: DraftState; champions: Map<string, Champion>; mine: boolean }) {
  const { side, draft, champions, mine } = props
  const picks = lineup(draft, side)
  return (
    <div className={`team ${side}`}>
      <h3>
        {SIDE_TR[side]} {mine && <span className="muted small">(sen)</span>}
      </h3>
      <div className="bans">
        {Array.from({ length: 5 }, (_, i) => {
          const ban = bans(draft, side)[i]
          return <ChampionIcon key={i} id={ban ?? null} name={ban ? champions.get(ban)?.name : undefined} size={32} dim />
        })}
      </div>
      {ROLES.map((role, i) => (
        <div key={role} className="slot">
          <ChampionIcon id={picks[i]} name={picks[i] ? champions.get(picks[i])?.name : undefined} side={side} size={44} />
          <div>
            <div className="muted small">{ROLE_TR[role]}</div>
            <div>{picks[i] ? champions.get(picks[i])?.name : '—'}</div>
          </div>
        </div>
      ))}
    </div>
  )
}
