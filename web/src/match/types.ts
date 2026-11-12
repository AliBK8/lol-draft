// Maçın dondurulmuş belgesi (DESIGN.md §4.9, §8.15, §8.16): MatchReady ve GET /api/matches/{id} aynı biçim.
// Sunucudaki Application/Matches/MatchReplay.cs ile birebir; sözleşme kırılırsa `version` artar.
import type { Role, Seat, Side } from '../api/types'

export type Lane = 'top' | 'mid' | 'bot'
export type River = 'top' | 'bottom'
export type ObjectiveKind = 'dragon' | 'herald' | 'baron' | 'elder' | 'finalFight'

interface Base {
  /** Oyun saniyesi. */
  t: number
  /** Boşsa log'a yazılmaz, sadece haritayı yönlendirir. */
  text: string
}

export type MatchEvent =
  | (Base & { type: 'kill'; side: Side; killer: string; victim: string; until: number; lane: Lane | null; bounty: number })
  | (Base & { type: 'tower'; side: Side; lane: Lane; tier: 0 | 1 | 2 })
  | (Base & { type: 'plate' | 'cs' | 'surprise' | 'gankfail'; side: Side; lane: Lane })
  | (Base & { type: 'move'; participants: string[]; lane: Lane | null; river: River | null; lead: number; tail: number })
  | (Base & { type: 'skirmish'; side: Side; probability: number; river: River; blueCount: number; redCount: number })
  | (Base & { type: 'objective'; objective: ObjectiveKind; side: Side; probability: number })
  | (Base & { type: 'siege'; side: Side; lead: number; tail: number })
  | (Base & { type: 'split'; side: Side; lane: Lane; splitter: string; answerer: string | null })
  | (Base & { type: 'dragonSoul'; side: Side })
  | (Base & { type: 'throw'; side: Side; probability: number })
  | (Base & { type: 'nexus'; side: Side; probability: number | null })

export interface ChampionGold {
  champion: string
  side: Side
  gold: number
  cs: number
}

/** Dakika karesi: 10 şampiyon, her takım yuva sırasıyla (top, jungle, mid, bot, support). */
export interface GoldFrame {
  minute: number
  champions: ChampionGold[]
}

export interface RuleLine {
  rule: string
  side: Side
  context: string
  champion: string
  count: number
  impact: number
  text: string
}

export interface CarryLine {
  champion: string
  side: Side
  minute: number
  potentialShare: number
  text: string
}

export interface TeamGold {
  passive: number
  farm: number
  supportItem: number
  kill: number
  bounty: number
  assist: number
  plate: number
  tower: number
  objective: number
  total: number
}

export interface OffRoleLine {
  champion: string
  side: Side
  role: Role
  penalty: number
  goldDiff: number
  text: string
}

export interface Replay {
  version: number
  id: string
  gameNumber: number
  /** Bu maçta mavi olan koltuk. */
  blueTeam: Seat
  end: { minute: number; winner: Side; kind: 'siege' | 'finalFight'; winnerProbability: number }
  events: MatchEvent[]
  goldFrames: GoldFrame[]
  report: { rules: RuleLine[]; carries: CarryLine[]; blue: TeamGold; red: TeamGold; offRoles: OffRoleLine[] }
}
