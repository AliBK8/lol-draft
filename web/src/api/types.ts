// Sunucuyla JSON sözleşmesi (DESIGN.md §8.7, §8.12). Sunucudaki DTO'larla birebir; ad değişirse ikisi birlikte değişir.

export type Seat = 'A' | 'B'
export type Side = 'blue' | 'red'
export type Format = 'bo1' | 'bo3' | 'bo5'
export type Role = 'top' | 'jungle' | 'mid' | 'bot' | 'support'

export const ROLES: readonly Role[] = ['top', 'jungle', 'mid', 'bot', 'support']

/** POST /api/lobbies ve /join cevabı: koltuk + misafir token'ı (2 saat). */
export interface Ticket {
  code: string
  seat: Seat
  token: string
}

/** GET /api/champions: istemcinin bildiği her şey (gizli motor sayıları yok, §0). */
export interface Champion {
  id: string
  name: string
  roles: Role[]
  /** Yuva → rol dışı ceza (0 = kendi rolü; §16.8). */
  offRole: Record<Role, number>
}

export type DraftPhase = 'ban1' | 'pick1' | 'ban2' | 'pick2' | 'swap' | 'completed'

export interface DraftAction {
  step: number
  side: Side
  kind: 'ban' | 'pick'
  /** Zaman aşımındaki ban boş geçebilir. */
  champion: string | null
  byTimeout: boolean
}

export interface DraftState {
  phase: DraftPhase
  turn: Side | null
  step: 'ban' | 'pick' | null
  /** Bu adımın bitiş anı (sunucu saati); kalan = deadline − serverNow. */
  deadline: string | null
  actions: DraftAction[]
  /** Swap'ta yuva sırası: top, jungle, mid, bot, support. */
  blueLineup: string[] | null
  redLineup: string[] | null
  blueLocked: boolean
  redLocked: boolean
  fearlessLocks: string[]
}

export interface SeatState {
  seat: Seat
  connected: boolean
  /** Bağlı değilse ne zamandan beri (hükmen = absentSince + 2 dk). */
  absentSince: string | null
}

export interface Game {
  number: number
  /** O maçta mavi olan koltuk. */
  blue: Seat
  winner: Seat
  /** Tekrar izleme kimliği (GET /api/matches/{match}). */
  match: string
}

/** Lobi durumu: GET /api/lobbies/{code} ve hub'ın LobbyUpdated olayı aynı biçim. */
export interface LobbyState {
  code: string
  /** Her değişimde artar; elindekinden küçük ya da eşitse at (§8.12). */
  revision: number
  format: Format
  fearless: boolean
  isFull: boolean
  gameNumber: number
  blueTeam: Seat
  winsA: number
  winsB: number
  winner: Seat | null
  endReason: 'played' | 'forfeit' | null
  ready: Seat[]
  seats: SeatState[]
  draft: DraftState | null
  games: Game[]
  serverNow: string
}

export interface LobbyResponse {
  yourSeat: Seat
  lobby: LobbyState
}

/** Hata gövdesi (her uç noktada aynı, §8.7). */
export interface ApiErrorBody {
  error: string
  message: string
}
