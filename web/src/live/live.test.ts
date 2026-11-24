import { describe, expect, it } from 'vitest'
import type { Champion, DraftState, LobbyState } from '../api/types'
import { clockOffset, remainingMs, secondsLeft } from './clock'
import {
  banSlots,
  DRAFT_ORDER,
  isMyTurn,
  latestBan,
  lineup,
  mySide,
  offRolePenalty,
  opponentChoice,
  unavailable,
} from './draft'
import { hasChosenPlan, planFits, planName, PLANS } from './plans'
import { newer } from './revision'

const lobby = (revision: number, extra: Partial<LobbyState> = {}): LobbyState => ({
  code: 'AAA', revision, format: 'bo3', fearless: false, isFull: true, gameNumber: 1, blueTeam: 'A',
  winsA: 0, winsB: 0, winner: null, endReason: null, ready: [], seats: [], draft: null, games: [],
  serverNow: '2026-11-12T12:00:00Z', ...extra,
})

const draft = (extra: Partial<DraftState> = {}): DraftState => ({
  phase: 'ban1', turn: 'blue', step: 'ban', deadline: null, actions: [], blueLineup: null, redLineup: null,
  blueLocked: false, redLocked: false, fearlessLocks: [], bluePlanChosen: false, redPlanChosen: false,
  bluePlanFits: null, redPlanFits: null, ...extra,
})

describe('revision', () => {
  it('sadece daha büyük sürüm kabul edilir', () => {
    expect(newer(null, lobby(1))).toBe(true)
    expect(newer(lobby(5), lobby(6))).toBe(true)
    expect(newer(lobby(5), lobby(5))).toBe(false)
    expect(newer(lobby(5), lobby(4))).toBe(false)
  })
})

describe('saat', () => {
  // Yerel saat sunucudan 5 sn ileri: geri sayım yine sunucuya göre 30 sn.
  it('yerel saat kaymış olsa da kalan süre sunucuya göre', () => {
    const local = Date.parse('2026-11-12T12:00:05Z')
    const offset = clockOffset('2026-11-12T12:00:00Z', local)

    expect(offset).toBe(-5000)
    expect(remainingMs('2026-11-12T12:00:30Z', offset, local)).toBe(30000)
    expect(remainingMs('2026-11-12T12:00:30Z', offset, local + 29_800)).toBe(200)
  })

  it('süre dolunca 0, bitiş yoksa null; saniye yukarı yuvarlanır', () => {
    expect(remainingMs('2026-11-12T12:00:00Z', 0, Date.parse('2026-11-12T12:01:00Z'))).toBe(0)
    expect(remainingMs(null, 0, 0)).toBeNull()
    expect(secondsLeft(200)).toBe(1)
    expect(secondsLeft(0)).toBe(0)
    expect(secondsLeft(null)).toBeNull()
  })
})

describe('draft', () => {
  it('sıra 20 adım: 6 ban, 6 pick, 4 ban, 4 pick; her takım 5 ban 5 pick', () => {
    expect(DRAFT_ORDER).toHaveLength(20)
    for (const side of ['blue', 'red'] as const) {
      expect(DRAFT_ORDER.filter((s) => s.side === side && s.kind === 'ban')).toHaveLength(5)
      expect(DRAFT_ORDER.filter((s) => s.side === side && s.kind === 'pick')).toHaveLength(5)
    }
  })

  it('renk koltuğa göre: mavi koltuk B ise A kırmızı', () => {
    expect(mySide(lobby(1, { blueTeam: 'A' }), 'A')).toBe('blue')
    expect(mySide(lobby(1, { blueTeam: 'B' }), 'A')).toBe('red')
  })

  it('boş geçen ban (süre doldu ya da "Ban yok") oynanmamış yuvadan ayrı', () => {
    const d = draft({
      actions: [
        { step: 0, side: 'blue', kind: 'ban', champion: 'zed', byTimeout: false },
        { step: 1, side: 'red', kind: 'ban', champion: null, byTimeout: true },
        { step: 2, side: 'blue', kind: 'ban', champion: null, byTimeout: false },
      ],
    })

    expect(banSlots(d, 'blue')).toEqual([
      { kind: 'banned', champion: 'zed' },
      { kind: 'none' },
      { kind: 'open' },
      { kind: 'open' },
      { kind: 'open' },
    ])
    expect(banSlots(d, 'red').map((s) => s.kind)).toEqual(['none', 'open', 'open', 'open', 'open'])
  })

  it('rakibin ön izlemesi sadece sıra ondayken, aynı adımda ve şampiyon alınmamışsa görünür', () => {
    const d = draft({ turn: 'red', actions: [{ step: 0, side: 'blue', kind: 'ban', champion: 'zed', byTimeout: false }] })
    const red = (champion: string | null, step = 1) => ({ side: 'red' as const, step, champion })

    expect(opponentChoice(d, 'blue', red('ahri'))).toBe('ahri')
    expect(opponentChoice(d, 'blue', null)).toBeNull()
    expect(opponentChoice(d, 'blue', red(null))).toBeNull() // bıraktı
    expect(opponentChoice(d, 'blue', red('ahri', 0))).toBeNull() // eski adımın ön izlemesi
    expect(opponentChoice(d, 'blue', red('zed'))).toBeNull() // bu arada ban'landı
    expect(opponentChoice(d, 'red', red('ahri'))).toBeNull() // kendi rengim (başka sekme)
    expect(opponentChoice(draft({ turn: 'blue' }), 'blue', red('ahri', 0))).toBeNull() // sıra onda değil
  })

  it('sıra bende mi', () => {
    expect(isMyTurn(draft({ turn: 'blue' }), 'blue')).toBe(true)
    expect(isMyTurn(draft({ turn: 'red' }), 'blue')).toBe(false)
    expect(isMyTurn(draft({ turn: null, step: null, phase: 'swap' }), 'blue')).toBe(false)
  })

  it('ban, pick ve fearless kilidi tıklanamaz; boş geçen ban sayılmaz', () => {
    const d = draft({
      actions: [
        { step: 0, side: 'blue', kind: 'ban', champion: 'zed', byTimeout: false },
        { step: 1, side: 'red', kind: 'ban', champion: null, byTimeout: true },
      ],
      fearlessLocks: ['jinx'],
    })

    expect([...unavailable(d)].sort()).toEqual(['jinx', 'zed'])
  })

  it('ban animasyonu: sadece görülenlerden sonraki ban; boş geçen ban ve pick sayılmaz', () => {
    const actions = [
      { step: 0, side: 'blue' as const, kind: 'ban' as const, champion: 'zed', byTimeout: false },
      { step: 1, side: 'red' as const, kind: 'ban' as const, champion: null, byTimeout: true },
      { step: 2, side: 'blue' as const, kind: 'ban' as const, champion: 'ahri', byTimeout: false },
      { step: 3, side: 'red' as const, kind: 'pick' as const, champion: 'jinx', byTimeout: false },
    ]

    // Sayfa açılınca 2 hamle görülmüştü: yeni gelenlerin son ban'ı Ahri (sonraki pick ban değil).
    expect(latestBan(actions, 2)?.champion).toBe('ahri')
    // Rejoin: hepsi görülmüş sayılır, eski ban'lar yeniden oynamaz.
    expect(latestBan(actions, 4)).toBeNull()
    // Yeni gelen tek hamle süresi dolmuş boş ban: animasyon yok.
    expect(latestBan(actions.slice(0, 2), 1)).toBeNull()
  })

  it('pick sırası yuva sırası; swap fazında sunucunun dizilimi', () => {
    const picks = draft({
      actions: [
        { step: 6, side: 'blue', kind: 'pick', champion: 'darius', byTimeout: false },
        { step: 7, side: 'red', kind: 'pick', champion: 'ashe', byTimeout: false },
        { step: 9, side: 'blue', kind: 'pick', champion: 'leesin', byTimeout: false },
      ],
    })
    expect(lineup(picks, 'blue')).toEqual(['darius', 'leesin', null, null, null])
    expect(lineup(picks, 'red')).toEqual(['ashe', null, null, null, null])

    const swap = draft({ phase: 'swap', blueLineup: ['a', 'b', 'c', 'd', 'e'] })
    expect(lineup(swap, 'blue')).toEqual(['a', 'b', 'c', 'd', 'e'])
  })

  it('rol dışı ceza sunucunun değeri', () => {
    const thresh: Champion = {
      id: 'thresh', name: 'Thresh', roles: ['support'],
      offRole: { top: 0.35, jungle: 0.35, mid: 0.3, bot: 0.3, support: 0 },
    }
    const champions = new Map([['thresh', thresh]])

    expect(offRolePenalty(champions, 'thresh', 'bot')).toBe(0.3)
    expect(offRolePenalty(champions, 'thresh', 'support')).toBe(0)
    expect(offRolePenalty(champions, null, 'top')).toBe(0)
  })
})

describe('oyun planı', () => {
  it('uyum takımın kendi tablosundan; plan fazı dışında boş', () => {
    const planning = draft({ phase: 'plan', bluePlanFits: { dragonLate: 'good' }, redPlanFits: { dragonLate: 'poor' } })
    expect(planFits(planning, 'blue').dragonLate).toBe('good')
    expect(planFits(planning, 'red').dragonLate).toBe('poor')
    expect(planFits(draft(), 'blue')).toEqual({})
  })

  it('seçim bayrağı tarafa göre; 7 plan kartı, adı bilinmeyen kimlik kendisi', () => {
    expect(hasChosenPlan(draft({ redPlanChosen: true }), 'red')).toBe(true)
    expect(hasChosenPlan(draft({ redPlanChosen: true }), 'blue')).toBe(false)
    expect(PLANS.map((p) => p.id)).toEqual(['balanced', 'dragonLate', 'earlyPressure', 'botPressure', 'split', 'fastFinish', 'invade'])
    expect(planName('fastFinish')).toBe('Hızlı bitir')
  })
})
