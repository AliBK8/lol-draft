import { useEffect, useMemo, useState } from 'react'
import type { Champion, DraftState, Role, Side } from '../api/types'
import { ROLES } from '../api/types'
import { ChampionIcon } from '../components/ChampionIcon'
import { remainingMs } from '../live/clock'
import { bans, DRAFT_ORDER, isMyTurn, latestBan, lineup, ROLE_TR, unavailable } from '../live/draft'
import type { ActionResult, HubMethod } from '../live/useLobbyConnection'

const PHASE_TR: Record<string, string> = { ban1: 'Ban 1', pick1: 'Pick 1', ban2: 'Ban 2', pick2: 'Pick 2' }
const SIDE_TR: Record<Side, string> = { blue: 'Mavi', red: 'Kırmızı' }
/** Bir adımın süresi (Domain DraftTimings.Default): süre çubuğu bunun ne kadarının kaldığını gösterir. */
const STEP_SECONDS = 30

interface Props {
  draft: DraftState
  me: Side
  champions: Map<string, Champion>
  offsetMs: number
  act: (method: HubMethod, ...args: string[]) => Promise<ActionResult>
}

// Ban/pick (DESIGN.md §2, görünüm §8.27): turnuva yayını gibi. Üstte süre ve ban şeridi, iki yanda takımların pick
// kartları, ortada havuz. Sıra sendeyse havuzdan seç (kendi yuvanda ön izleme), onayla. Kurallar sunucuda; burada
// tıklanamayanlar sadece görüntü. Maç başına bir kez takılır (LobbyScreen'de anahtar = maç numarası).
export function DraftBoard({ draft, me, champions, offsetMs, act }: Props) {
  // Ekran açıldığında görülen hamleler: ban animasyonu sadece bundan sonrakiler için (rejoin'de eskiler oynamaz).
  const [seenAtOpen] = useState(() => draft.actions.length)
  const flash = latestBan(draft.actions, seenAtOpen)
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
      <div className={`draft-head ${draft.turn ?? ''}`}>
        <span className={`draft-team blue${draft.turn === 'blue' ? ' active' : ''}`}>
          {SIDE_TR.blue}
          {me === 'blue' && <small> (sen)</small>}
        </span>
        <div className="draft-status">
          <span className="draft-phase">{PHASE_TR[draft.phase] ?? draft.phase}</span>
          <span className={`draft-turn ${draft.turn ?? ''}`}>
            {myTurn ? 'Sıra sende' : `Sıra ${draft.turn ? SIDE_TR[draft.turn] : ''}`}: {next?.kind === 'ban' ? 'BAN' : 'PICK'}
          </span>
        </div>
        <span className={`draft-team red${draft.turn === 'red' ? ' active' : ''}`}>
          {me === 'red' && <small>(sen) </small>}
          {SIDE_TR.red}
        </span>
        <StepTimer deadline={draft.deadline} offsetMs={offsetMs} side={draft.turn} />
      </div>

      <BanStrip draft={draft} champions={champions} me={me} preview={myTurn && draft.step === 'ban' ? choice : null} />

      <div className="draft-main">
        <TeamColumn side="blue" draft={draft} champions={champions} preview={me === 'blue' && myTurn ? choice : null} />

        <div className="pool">
          {flash?.champion && (
            <BanFlash
              key={flash.step}
              champion={flash.champion}
              name={champions.get(flash.champion)?.name ?? flash.champion}
              side={flash.side}
            />
          )}
          <div className="pool-filters">
            <button className={role === 'all' ? 'on' : ''} onClick={() => setRole('all')}>
              Hepsi
            </button>
            {ROLES.map((r) => (
              <button
                key={r}
                className={`role-filter${role === r ? ' on' : ''}`}
                onClick={() => setRole(r)}
                title={ROLE_TR[r]}
                aria-label={ROLE_TR[r]}
              >
                <img src={`/roles/${r}.png`} alt="" width={20} height={20} />
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
                  <span className="pool-portrait">
                    <ChampionIcon id={c.id} name={c.name} size={60} dim={out} />
                    {locked && <span className="lock-badge" aria-label="fearless kilidi">🔒</span>}
                  </span>
                  <span>{c.name}</span>
                </button>
              )
            })}
          </div>
          <div className="pool-confirm">
            <button className={`primary lock-in ${draft.step ?? ''}`} disabled={!myTurn || !choice || busy} onClick={confirm}>
              {draft.step === 'ban' ? 'Banla' : 'Kilitle'}
              {choice ? `: ${champions.get(choice)?.name}` : ''}
            </button>
            {error && <span className="error">{error}</span>}
          </div>
        </div>

        <TeamColumn side="red" draft={draft} champions={champions} preview={me === 'red' && myTurn ? choice : null} />
      </div>
    </div>
  )
}

/**
 * Ban anı (2 sn, havuzun üstünde, tıklamayı engellemez): splash renkli belirir, griye döner, kırmızı çizgi çekilir,
 * "BANLANDI" damgası basılır, söner. Zamanlayıcı yok: her yeni ban yeni anahtarla takılır, CSS animasyonu bir kez
 * oynar ve görünmez kalır (sonraki ban'a kadar). Durum iki oyuncuya aynı anda geldiği için ikisi de aynı anı görür.
 */
function BanFlash({ champion, name, side }: { champion: string; name: string; side: Side }) {
  return (
    <div className={`ban-flash ${side}`} role="status">
      <img className="ban-flash-art" src={`/champions/splash/${champion}.jpg`} alt="" />
      <span className="ban-flash-slash" />
      <div className="ban-flash-text">
        <strong>BANLANDI</strong>
        <span>
          {name} · {SIDE_TR[side]}
        </span>
      </div>
    </div>
  )
}

/** Kalan süre: büyük sayı ve sıradakinin renginde boşalan çubuk (son 5 sn kırmızı, nabız). */
function StepTimer({ deadline, offsetMs, side }: { deadline: string | null; offsetMs: number; side: Side | null }) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!deadline) return
    const timer = setInterval(() => setNow(Date.now()), 250)
    return () => clearInterval(timer)
  }, [deadline])

  const ms = remainingMs(deadline, offsetMs, now)
  if (ms === null) return null
  const seconds = Math.ceil(ms / 1000)
  const urgent = seconds <= 5
  const fraction = Math.max(0, Math.min(1, ms / (STEP_SECONDS * 1000)))
  return (
    <>
      <span className={`draft-clock${urgent ? ' urgent' : ''}`}>{seconds}</span>
      <div className="draft-bar-track">
        <div className={`draft-bar-fill ${side ?? ''}${urgent ? ' urgent' : ''}`} style={{ width: `${fraction * 100}%` }} />
      </div>
    </>
  )
}

/**
 * Ban şeridi (bandın altı): mavinin 5 ban'ı solda, mavinin üstünde; kırmızınınki sağda, ayna gibi (ilk ban en sağda).
 * Sıradaki ban yuvası parlar; sıra bendeyse seçtiğim orada yarı saydam görünür.
 */
function BanStrip(props: { draft: DraftState; champions: Map<string, Champion>; me: Side; preview: string | null }) {
  return (
    <div className="ban-strip">
      {(['blue', 'red'] as const).map((side) => (
        <BanRow key={side} side={side} {...props} preview={props.me === side ? props.preview : null} />
      ))}
    </div>
  )
}

function BanRow(props: { side: Side; draft: DraftState; champions: Map<string, Champion>; preview: string | null }) {
  const { side, draft, champions, preview } = props
  const teamBans = bans(draft, side)
  const next = draft.step ? DRAFT_ORDER[draft.actions.length] : undefined
  const active = next?.side === side && next.kind === 'ban' ? teamBans.length : -1
  return (
    <div className={`ban-row ${side}`} aria-label={`${SIDE_TR[side]} ban'ları`}>
      {Array.from({ length: 5 }, (_, i) => {
        const ban = teamBans[i] ?? (i === active ? preview : null)
        const state = teamBans[i] ? 'filled' : i === active ? (preview ? 'active preview' : 'active') : ''
        return (
          <span key={`${i}-${ban ?? ''}-${state}`} className={`ban-slot ${state}`}>
            <ChampionIcon id={ban} name={ban ? champions.get(ban)?.name : undefined} size={40} dim={state === 'filled'} />
          </span>
        )
      })}
    </div>
  )
}

/** Takım: 5 pick kartı. Sıradaki yuva parlar; sıra bendeyse seçtiğim orada yarı saydam görünür. */
function TeamColumn(props: { side: Side; draft: DraftState; champions: Map<string, Champion>; preview: string | null }) {
  const { side, draft, champions, preview } = props
  const picks = lineup(draft, side)
  const next = draft.step ? DRAFT_ORDER[draft.actions.length] : undefined
  const activePick = next?.side === side && next.kind === 'pick' ? picks.filter(Boolean).length : -1

  return (
    <div className={`team ${side}`}>
      {ROLES.map((role, i) => {
        const champion = picks[i] ?? (i === activePick ? preview : null)
        const state = picks[i] ? 'filled' : i === activePick ? (preview ? 'active preview' : 'active') : ''
        return (
          <PickCard
            key={`${role}-${champion ?? ''}-${state}`}
            side={side}
            role={role}
            champion={champion}
            name={champion ? champions.get(champion)?.name : undefined}
            state={state}
          />
        )
      })}
    </div>
  )
}

/** Pick kartı: yatay splash (karakter ortada, tamamı görünür), rol ikonu ve ad. */
function PickCard(props: { side: Side; role: Role; champion: string | null; name?: string; state: string }) {
  const { side, role, champion, name, state } = props
  return (
    <div className={`pick-card ${side} ${state}`}>
      {champion && <img className="pick-art" src={`/champions/splash/${champion}.jpg`} alt="" loading="lazy" />}
      <div className="pick-info">
        <img className="pick-role" src={`/roles/${role}.png`} alt={ROLE_TR[role]} title={ROLE_TR[role]} width={18} height={18} />
        <span className="pick-name">{name ?? (state.startsWith('active') ? 'seçiyor…' : '')}</span>
      </div>
    </div>
  )
}
