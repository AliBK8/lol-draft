// Skor bandı ve kadrolar (UYGULAMA.md §8.28): T anına kadar olan olaylardan ve dakika altın karelerinden türetilir.
// Saf: aynı T aynı tablo (geri sarma, zaman çizelgesi bedava). Asist motorda olay değil, gösterilmez.
import type { Role, Side } from '../api/types'
import type { MatchState } from './timeline'
import { roster } from './timeline'
import type { ObjectiveKind, Replay } from './types'

export interface ChampionLine {
  id: string
  side: Side
  role: Role
  slot: number
  kills: number
  deaths: number
  /** Toplam altın (dakika kareleri arasında doğrusal). */
  gold: number
  /** Ölüyse yeniden doğmaya kalan oyun saniyesi (yukarı yuvarlanmış), yaşıyorsa null. */
  respawnIn: number | null
}

export interface TeamLine {
  kills: number
  gold: number
  /** Yıktığı kule (rakibin kaybettiği). */
  towers: number
  /** Aldığı canavarlar (son savaş sayılmaz). */
  objectives: Record<Exclude<ObjectiveKind, 'finalFight'>, number>
}

export interface Scoreboard {
  /** Yuva sırasıyla: mavi 0-4, kırmızı 5-9. */
  champions: ChampionLine[]
  teams: Record<Side, TeamLine>
}

const other = (side: Side): Side => (side === 'blue' ? 'red' : 'blue')

/** Şampiyonun T anındaki altını: iki dakika karesi arasında doğrusal (HUD'daki takım farkıyla aynı yöntem). */
function goldAt(replay: Replay, champion: string, t: number): number {
  const frames = replay.goldFrames
  if (frames.length === 0) return 0
  const minute = t / 60
  const i = Math.min(frames.length - 1, Math.max(0, Math.floor(minute)))
  const j = Math.min(frames.length - 1, i + 1)
  const a = Math.min(1, minute - i)
  const at = (k: number) => frames[k].champions.find((c) => c.champion === champion)?.gold ?? 0
  return Math.round(at(i) * (1 - a) + at(j) * a)
}

export function scoreboard(replay: Replay, state: MatchState): Scoreboard {
  const t = state.t
  const kills = new Map<string, number>()
  const deaths = new Map<string, number>()
  const objectives: Record<Side, TeamLine['objectives']> = {
    blue: { dragon: 0, herald: 0, baron: 0, elder: 0 },
    red: { dragon: 0, herald: 0, baron: 0, elder: 0 },
  }
  for (const e of replay.events) {
    if (e.t > t) break
    if (e.type === 'kill') {
      kills.set(e.killer, (kills.get(e.killer) ?? 0) + 1)
      deaths.set(e.victim, (deaths.get(e.victim) ?? 0) + 1)
    } else if (e.type === 'objective' && e.objective !== 'finalFight') {
      objectives[e.side][e.objective]++
    }
  }

  const champions = [...roster(replay)].map(([id, c]): ChampionLine => {
    const until = state.dead.get(id)
    return {
      id,
      ...c,
      kills: kills.get(id) ?? 0,
      deaths: deaths.get(id) ?? 0,
      gold: goldAt(replay, id, t),
      respawnIn: until === undefined ? null : Math.max(1, Math.ceil(until - t)),
    }
  })

  const team = (side: Side): TeamLine => {
    const lost = state.towers.lost[other(side)]
    return {
      kills: state.kills[side],
      gold: champions.filter((c) => c.side === side).reduce((sum, c) => sum + c.gold, 0),
      towers: lost.top + lost.mid + lost.bot,
      objectives: objectives[side],
    }
  }
  return { champions, teams: { blue: team('blue'), red: team('red') } }
}

/** "8,2k" gibi kısa altın (kadrolar); 1000 altında tam sayı. */
export function shortGold(gold: number): string {
  return gold < 1000 ? String(gold) : `${(gold / 1000).toLocaleString('tr-TR', { maximumFractionDigits: 1 })}k`
}
