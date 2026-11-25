import { useEffect, useMemo, useRef, useState } from 'react'
import type { Champion, DraftPreview, DraftState, Role, Side } from '../api/types'
import { ROLES } from '../api/types'
import { ChampionIcon } from '../components/ChampionIcon'
import { SeriesChips, SeriesPips } from '../components/SeriesHeader'
import { teamLabel, type SeriesHeader } from '../live/seriesHeader'
import { remainingMs } from '../live/clock'
import { banSlots, DRAFT_ORDER, isMyTurn, latestBan, lineup, opponentChoice, ROLE_TR, unavailable } from '../live/draft'
import type { ActionResult, HubMethod } from '../live/useLobbyConnection'

const PHASE_TR: Record<string, string> = { ban1: 'Ban 1', pick1: 'Pick 1', ban2: 'Ban 2', pick2: 'Pick 2' }
const SIDE_TR: Record<Side, string> = { blue: 'Mavi', red: 'Kırmızı' }
/** Bir adımın süresi (Domain DraftTimings.Default): süre çubuğu bunun ne kadarının kaldığını gösterir. */
const STEP_SECONDS = 30
/** Ön izleme en fazla bu aralıkla gider (§8.32): saniyede en çok 4; sunucunun sınırı 5. */
const PREVIEW_INTERVAL_MS = 300
/** Havuzdaki "Ban yok" kutusunun seçim değeri (§8.33); şampiyon kimliği olamaz (kimlikler küçük harf ve rakam). */
const NO_BAN = '-'

interface Props {
  draft: DraftState
  me: Side
  champions: Map<string, Champion>
  offsetMs: number
  act: (method: HubMethod, ...args: string[]) => Promise<ActionResult>
  /** Rakibin son ön izlemesi (geçerli mi, burada karar verilir). */
  opponentPreview: DraftPreview | null
  /** Seçimimi rakibe ön izleme olarak gönderir. */
  sendPreview: (champion: string | null) => void
  /** Seri bilgisi (adlar, seri skoru, bağlantı): ayrı seri bandı yerine bu ekranın bandında (§8.37). */
  header?: SeriesHeader
}

// Ban/pick (DESIGN.md §2, görünüm §8.27): turnuva yayını gibi. Üstte süre ve ban şeridi, iki yanda takımların pick
// kartları, ortada havuz. Sıra sendeyse havuzdan seç (kendi yuvanda ön izleme, rakip de görür §8.32), onayla.
// Kurallar sunucuda; burada tıklanamayanlar sadece görüntü. Maç başına bir kez takılır (LobbyScreen'de anahtar = maç numarası).
export function DraftBoard({ draft, me, champions, offsetMs, act, opponentPreview, sendPreview, header }: Props) {
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

  // Seçim sırası değişince (rakip oynadı, süre doldu) eski seçim düşer; "Ban yok" sadece ban adımında geçerli.
  const choice =
    selected === NO_BAN ? (draft.step === 'ban' ? NO_BAN : null) : selected && !taken.has(selected) ? selected : null
  // Yuvada ön izlenen ve rakibe giden şampiyon: "Ban yok" seçiliyken yok (yuva boş görünür).
  const champion = choice === NO_BAN ? null : choice

  // Ön izleme rakibe (§8.32): seçim ya da adım değişince gider, en fazla 300 ms'de bir. Arada değişenlerden sadece
  // sonuncusu gider: zamanlayıcı her değişiklikte yeniden kurulur ama hedef an (son gönderim + 300 ms) aynı kalır,
  // hızlı tıklama gönderimi ertelemez. Adım da bağımlılıkta: art arda iki adım bendeyse seçim yeni adımla yeniden gider.
  const step = draft.actions.length
  const lastSentAt = useRef(0)
  useEffect(() => {
    if (!myTurn) return
    const wait = Math.max(0, lastSentAt.current + PREVIEW_INTERVAL_MS - Date.now())
    const timer = setTimeout(() => {
      lastSentAt.current = Date.now()
      sendPreview(champion)
    }, wait)
    return () => clearTimeout(timer)
  }, [champion, myTurn, step, sendPreview])

  // Sıradaki yuvada görünen ön izleme: sıra bendeyse benim seçimim, rakipteyse onunki (hâlâ geçerliyse).
  const shown = myTurn ? champion : opponentChoice(draft, me, opponentPreview)

  async function confirm() {
    if (!choice || !draft.step) return
    setBusy(true)
    const result = choice === NO_BAN ? await act('SkipBan') : await act(draft.step === 'ban' ? 'Ban' : 'Pick', choice)
    setBusy(false)
    if (result.ok) {
      setSelected(null)
      setError(null)
    } else {
      setError(result.message)
    }
  }

  const next = DRAFT_ORDER[draft.actions.length]
  // Takım plakası: sırası olanınki parlar (ad tam opak). Ban önizlemesi sadece sırası olanın yuvasında.
  const plate = (side: Side) => (
    <div className={`hud-team draft-plate ${side}${draft.turn === side ? ' active' : ''}`}>
      <span className="hud-id draft-team">
        <span className="hud-name">
          <span className="hud-label draft-team-label">{teamLabel(side, header)}</span>
          <SeriesPips side={side} header={header} />
          {me === side && <span className="you-tag">Sen</span>}
        </span>
        {/* Bağlantı sadece koparsa (quiet): "Bağlı değil · hükmen N sn". */}
        {header?.teams[side].presence}
      </span>
      <BanRow
        side={side}
        draft={draft}
        champions={champions}
        preview={draft.step === 'ban' && draft.turn === side ? shown : null}
      />
    </div>
  )
  return (
    <div className="draft">
      {/* Yayın tabelası (§8.37, §8.41): maç ekranıyla aynı dil. İki takım plakası (ad + seri noktaları + "Sen",
          koparsa bağlantı, altında o takımın 5 ban'ı) ve ortada ters yamuk saat parçası (rozetler + faz, süre, sıra,
          süre çubuğu). Ayrı ban şeridi yok: ban'lar takımının plakasında. */}
      <div className="hud draft-hud">
        {plate('blue')}
        <div className="hud-center draft-center">
          <span className="draft-phase">
            <SeriesChips header={header} />
            {/* "Ban"/"Pick" İngilizce sözcük: sayfa Türkçe (lang=tr) olduğu için büyük harfe çevirme "PİCK" yapıyordu. */}
            <span lang="en">{PHASE_TR[draft.phase] ?? draft.phase}</span>
          </span>
          <StepTimer deadline={draft.deadline} offsetMs={offsetMs} side={draft.turn} />
          <span className={`draft-turn ${draft.turn ?? ''}`}>
            {myTurn ? 'Sıra sende' : `Sıra ${draft.turn ? teamLabel(draft.turn, header) : ''}`}: {next?.kind === 'ban' ? 'BAN' : 'PICK'}
          </span>
        </div>
        {plate('red')}
      </div>

      <div className="draft-main">
        <TeamColumn side="blue" draft={draft} champions={champions} preview={draft.turn === 'blue' ? shown : null} />

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
            {/* LoL'deki "None": ban adımında ilk kutu; seç, sonra onayla (tek tıkla geçilmesin). Filtreden bağımsız. */}
            {draft.step === 'ban' && (
              <button
                className={['pool-item', 'no-ban', choice === NO_BAN ? 'picked' : ''].join(' ')}
                disabled={!myTurn}
                onClick={() => setSelected(NO_BAN)}
                title="Bu adımda ban atma"
              >
                <span className="pool-portrait">
                  <NoBanIcon size={60} />
                </span>
                <span>Ban yok</span>
              </button>
            )}
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
              {choice === NO_BAN
                ? 'Onayla: Ban yok'
                : `${draft.step === 'ban' ? 'Banla' : 'Kilitle'}${choice ? `: ${champions.get(choice)?.name}` : ''}`}
            </button>
            {error && <span className="error">{error}</span>}
          </div>
        </div>

        <TeamColumn side="red" draft={draft} champions={champions} preview={draft.turn === 'red' ? shown : null} />
      </div>
    </div>
  )
}

/**
 * Ban anı (2 sn, havuzun üstünde, tıklamayı engellemez): splash renkli belirir, griye döner, kırmızı çizgi çekilir,
 * altta şampiyon ve takım belirir, söner ("BANLANDI" damgası kaldırıldı, 2026-11-23: kaba duruyordu). Zamanlayıcı yok: her yeni ban yeni anahtarla takılır, CSS animasyonu bir kez
 * oynar ve görünmez kalır (sonraki ban'a kadar). Durum iki oyuncuya aynı anda geldiği için ikisi de aynı anı görür.
 */
function BanFlash({ champion, name, side }: { champion: string; name: string; side: Side }) {
  return (
    <div className={`ban-flash ${side}`} role="status">
      <img className="ban-flash-art" src={`/champions/splash/${champion}.jpg`} alt="" />
      <span className="ban-flash-slash" />
      <div className="ban-flash-text">
        <span className="sr-only">Banlandı:</span>
        <span>
          {name} · {SIDE_TR[side]}
        </span>
      </div>
    </div>
  )
}

/** Kalan süre: büyük sayı ve sıradakinin renginde boşalan çubuk (son 5 sn kırmızı, nabız). Plan fazı da kullanır
 *  (§8.43: 20 sn, renksiz çubuk). */
export function StepTimer({
  deadline,
  offsetMs,
  side,
  totalSeconds = STEP_SECONDS,
}: {
  deadline: string | null
  offsetMs: number
  side: Side | null
  totalSeconds?: number
}) {
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
  const fraction = Math.max(0, Math.min(1, ms / (totalSeconds * 1000)))
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
 * Takımın 5 ban'ı (§8.41: kendi plakasında, adının altında). Kırmızı ayna gibi (ilk ban en sağda). Sıradaki ban
 * yuvası parlar; sıradakinin ön izlemesi (benimki ya da rakibinki) orada yarı saydam görünür.
 */
function BanRow(props: { side: Side; draft: DraftState; champions: Map<string, Champion>; preview: string | null }) {
  const { side, draft, champions, preview } = props
  const slots = banSlots(draft, side)
  const next = draft.step ? DRAFT_ORDER[draft.actions.length] : undefined
  const active = next?.side === side && next.kind === 'ban' ? slots.filter((s) => s.kind !== 'open').length : -1
  return (
    <div className={`ban-row ${side}`} aria-label={`${SIDE_TR[side]} ban'ları`}>
      {slots.map((slot, i) => {
        // Boş geçen ban (süre doldu ya da "Ban yok"): oynanmamış yuvadan ayrı, ⊘ işaretiyle (§8.33).
        if (slot.kind === 'none') {
          return (
            <span key={`${i}-none`} className="ban-slot none" title="Ban yok" aria-label="Ban yok">
              <NoBanIcon size={40} />
            </span>
          )
        }
        const ban = slot.kind === 'banned' ? slot.champion : i === active ? preview : null
        const state = slot.kind === 'banned' ? 'filled' : i === active ? (preview ? 'active preview' : 'active') : ''
        return (
          <span key={`${i}-${ban ?? ''}-${state}`} className={`ban-slot ${state}`}>
            <ChampionIcon id={ban} name={ban ? champions.get(ban)?.name : undefined} size={40} dim={state === 'filled'} />
          </span>
        )
      })}
    </div>
  )
}

/** Boş ban işareti (⊘): süre doldu ya da "Ban yok" (§8.33). Portre boyunda, `champ` çerçevesiyle. */
function NoBanIcon({ size }: { size: number }) {
  return (
    <span className="champ none" style={{ width: size, height: size }} aria-hidden="true">
      <svg viewBox="0 0 24 24" width="50%" height="50%">
        <circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" strokeWidth="2" />
        <path d="M5.6 18.4 18.4 5.6" stroke="currentColor" strokeWidth="2" />
      </svg>
    </span>
  )
}

/** Takım: 5 pick kartı. Sıradaki yuva parlar; sıradakinin ön izlemesi (benimki ya da rakibinki) orada yarı saydam. */
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
