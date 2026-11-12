// Oynatıcının zaman çizelgesi (DESIGN.md §7, §8.19): ekrandaki her şey tek bir oyun saatinden (T, saniye) hesaplanır.
// Olay "uygulanmaz", T'ye kadar olanlar sayılır: sona atla, geri sar, hız değişimi bedava; mantık saf, testli.
import type { Role, Side } from '../api/types'
import { ROLES } from '../api/types'
import type { Lane, MatchEvent, Replay } from './types'

/** 1x: 20 oyun saniyesi / gerçek saniye (1 dk = 3 sn, §7). */
export const GAME_SECONDS_PER_SECOND = 20

/** Oynatmanın bittiği an: son olay (Nexus) + birkaç saniye nefes. */
export function endTime(replay: Replay): number {
  const last = replay.events.at(-1)?.t ?? replay.end.minute * 60
  return Math.ceil(last) + 4
}

/** Şampiyon → taraf ve rol. goldFrames yuva sırasıyla gelir (sözleşme, §8.19): mavi 0-4, kırmızı 5-9. */
export function roster(replay: Replay): Map<string, { side: Side; role: Role; slot: number }> {
  const frame = replay.goldFrames[0]
  const map = new Map<string, { side: Side; role: Role; slot: number }>()
  frame?.champions.forEach((c, i) => map.set(c.champion, { side: c.side, role: ROLES[i % 5], slot: i % 5 }))
  return map
}

export interface TowerState {
  /** [taraf][koridor] = o tarafın bu koridorda kaybettiği kule sayısı (0-3). */
  lost: Record<Side, Record<Lane, number>>
}

export interface MatchState {
  t: number
  kills: Record<Side, number>
  dragons: Record<Side, number>
  towers: TowerState
  /** Ölü şampiyon → yeniden doğma anı. */
  dead: Map<string, number>
  /** Log'a düşmüş olaylar (metinli), yeniden eskiye. */
  log: MatchEvent[]
  /** Mavi − kırmızı toplam altın (dakika kareleri arasında doğrusal). */
  goldDiff: number
  finished: boolean
}

const other = (side: Side): Side => (side === 'blue' ? 'red' : 'blue')

/** T anındaki durum. Olaylar zamana göre sıralı gelir (motor sıralar). */
export function stateAt(replay: Replay, t: number): MatchState {
  const kills = { blue: 0, red: 0 }
  const dragons = { blue: 0, red: 0 }
  const lost: TowerState['lost'] = { blue: { top: 0, mid: 0, bot: 0 }, red: { top: 0, mid: 0, bot: 0 } }
  const dead = new Map<string, number>()
  const log: MatchEvent[] = []

  for (const e of replay.events) {
    if (e.t > t) break
    if (e.text) log.push(e)
    switch (e.type) {
      case 'kill':
        kills[e.side]++
        if (e.until > t) dead.set(e.victim, e.until)
        else dead.delete(e.victim)
        break
      case 'objective':
        if (e.objective === 'dragon') dragons[e.side]++
        break
      case 'tower':
        // side = kuleyi yıkan; kaybeden karşı taraf. Kademe sırayla düşer (0 dış, 1 iç, 2 inhibitör).
        lost[other(e.side)][e.lane] = Math.max(lost[other(e.side)][e.lane], e.tier + 1)
        break
    }
  }
  // Süresi dolan ölümler: yeniden doğdu.
  for (const [id, until] of dead) if (until <= t) dead.delete(id)

  return {
    t,
    kills,
    dragons,
    towers: { lost },
    dead,
    log: log.reverse(),
    goldDiff: goldDiffAt(replay, t),
    finished: t >= endTime(replay),
  }
}

/** Mavi − kırmızı altın; dakika kareleri arasında doğrusal aradeğer (HUD her karede akıcı değişsin). */
export function goldDiffAt(replay: Replay, t: number): number {
  const frames = replay.goldFrames
  if (frames.length === 0) return 0
  const diff = (i: number) =>
    frames[i].champions.reduce((sum, c) => sum + (c.side === 'blue' ? c.gold : -c.gold), 0)
  const minute = t / 60
  const i = Math.min(frames.length - 1, Math.max(0, Math.floor(minute)))
  const j = Math.min(frames.length - 1, i + 1)
  const a = minute - i
  return Math.round(diff(i) * (1 - Math.min(1, a)) + diff(j) * Math.min(1, a))
}

/** "mm:ss" */
export function clock(t: number): string {
  const s = Math.max(0, Math.floor(t))
  return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`
}
