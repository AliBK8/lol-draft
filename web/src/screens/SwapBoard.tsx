import { useState } from 'react'
import type { Champion, DraftState, Side } from '../api/types'
import { ROLES } from '../api/types'
import { ChampionIcon } from '../components/ChampionIcon'
import { Countdown } from '../components/Countdown'
import { isLocked, lineup, offRolePenalty, otherSide, ROLE_TR } from '../live/draft'
import type { ActionResult, HubMethod } from '../live/useLobbyConnection'

interface Props {
  draft: DraftState
  me: Side
  champions: Map<string, Champion>
  offsetMs: number
  act: (method: HubMethod, ...args: string[]) => Promise<ActionResult>
}

// Swap (DESIGN.md §16.8): pick sırası yuva sırası oldu; iki yuvaya tıkla, yer değiştirsinler. Rol dışı yuva serbest ama
// cezalı ve oynak: ceza sunucunun değeri (/api/champions). Kilitle ya da süre dolsun; ikisi de kilitleyince maç oynanır.
export function SwapBoard({ draft, me, champions, offsetMs, act }: Props) {
  const [first, setFirst] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const locked = isLocked(draft, me)

  async function tap(index: number) {
    if (locked) return
    if (first === null) {
      setFirst(index)
      return
    }
    setFirst(null)
    if (first === index) return
    const result = await act('SwapRoles', ROLES[first], ROLES[index])
    setError(result.ok ? null : result.message)
  }

  async function lock() {
    const result = await act('LockLineup')
    setError(result.ok ? null : result.message)
  }

  return (
    <div className="swap">
      <div className="draft-bar">
        <span>Yuva değişimi</span>
        <Countdown deadline={draft.deadline} offsetMs={offsetMs} />
        <span className="muted small">İki yuvaya tıkla, yer değiştirsinler. Süre dolunca dizilim olduğu gibi kilitlenir.</span>
      </div>
      <div className="swap-main">
        <Lineup draft={draft} side={me} champions={champions} selected={first} onTap={locked ? undefined : tap} />
        <Lineup draft={draft} side={otherSide(me)} champions={champions} selected={null} />
      </div>
      <button className="primary" disabled={locked} onClick={lock}>
        {locked ? 'Kilitlendi, rakip bekleniyor' : 'Dizilimi kilitle'}
      </button>
      {error && <p className="error">{error}</p>}
    </div>
  )
}

function Lineup(props: {
  draft: DraftState
  side: Side
  champions: Map<string, Champion>
  selected: number | null
  onTap?: (index: number) => void
}) {
  const { draft, side, champions, selected, onTap } = props
  const picks = lineup(draft, side)
  return (
    <div className={`team ${side}`}>
      <h3>
        {side === 'blue' ? 'Mavi' : 'Kırmızı'} {!onTap && (isLocked(draft, side) ? '· kilitledi' : '· düzenliyor')}
      </h3>
      {ROLES.map((role, i) => {
        const penalty = offRolePenalty(champions, picks[i], role)
        const name = picks[i] ? champions.get(picks[i])?.name : undefined
        return (
          <button
            key={role}
            className={['slot', 'slot-button', selected === i ? 'picked' : ''].join(' ')}
            disabled={!onTap}
            onClick={() => onTap?.(i)}
          >
            <ChampionIcon id={picks[i]} name={name} side={side} size={44} />
            <div>
              <div className="muted small">{ROLE_TR[role]}</div>
              <div>{name ?? '—'}</div>
              {/* Rol dışı: güç −%X ve lane fazında oynak (kumar). Kendi rolündeyse etiket yok. */}
              {penalty > 0 && (
                <div className="offrole">
                  rol dışı −%{Math.round(penalty * 100)} · oynak
                </div>
              )}
            </div>
          </button>
        )
      })}
    </div>
  )
}
