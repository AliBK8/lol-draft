// Draft'ın istemci görünümü: sunucunun durumundan türetilir, kural koymaz (kurallar Domain'de, DESIGN.md §8.3).
// Burada sadece ekranın sorduğu sorular: hangi taraf benim, sıra bende mi, hangi şampiyon tıklanamaz, yuvalar.
import type { Champion, DraftState, LobbyState, Role, Seat, Side } from '../api/types'
import { ROLES } from '../api/types'

/** Turnuva sırası (Domain/DraftOrder.cs ile aynı): 6 ban, 6 pick, 4 ban, 4 pick. */
export const DRAFT_ORDER: readonly { side: Side; kind: 'ban' | 'pick' }[] = [
  ...(['blue', 'red', 'blue', 'red', 'blue', 'red'] as const).map((side) => ({ side, kind: 'ban' as const })),
  ...(['blue', 'red', 'red', 'blue', 'blue', 'red'] as const).map((side) => ({ side, kind: 'pick' as const })),
  ...(['red', 'blue', 'red', 'blue'] as const).map((side) => ({ side, kind: 'ban' as const })),
  ...(['red', 'blue', 'blue', 'red'] as const).map((side) => ({ side, kind: 'pick' as const })),
]

export const ROLE_TR: Record<Role, string> = {
  top: 'Top',
  jungle: 'Orman',
  mid: 'Mid',
  bot: 'Bot',
  support: 'Destek',
}

/** Bu maçta benim rengim: mavi koltuk lobi durumunda (renkler her maç değişir). */
export function mySide(lobby: LobbyState, seat: Seat): Side {
  return lobby.blueTeam === seat ? 'blue' : 'red'
}

export function otherSide(side: Side): Side {
  return side === 'blue' ? 'red' : 'blue'
}

export function isMyTurn(draft: DraftState, side: Side): boolean {
  return draft.turn === side && draft.step !== null
}

/** Ban'lı, seçilmiş ya da fearless'la kilitli: havuzda tıklanamaz (sunucu da reddeder; bu sadece görüntü). */
export function unavailable(draft: DraftState): Set<string> {
  const taken = draft.actions.flatMap((a) => (a.champion ? [a.champion] : []))
  return new Set([...taken, ...draft.fearlessLocks])
}

export function bans(draft: DraftState, side: Side): (string | null)[] {
  return draft.actions.filter((a) => a.side === side && a.kind === 'ban').map((a) => a.champion)
}

/** Pick sırası = yuva sırası (1. pick top, 2. pick orman…); swap'ta dizilim sunucudan gelir. */
export function lineup(draft: DraftState, side: Side): (string | null)[] {
  const fromServer = side === 'blue' ? draft.blueLineup : draft.redLineup
  if (fromServer) return fromServer
  const picks = draft.actions.filter((a) => a.side === side && a.kind === 'pick').map((a) => a.champion)
  return ROLES.map((_, i) => picks[i] ?? null)
}

export function isLocked(draft: DraftState, side: Side): boolean {
  return side === 'blue' ? draft.blueLocked : draft.redLocked
}

/**
 * Yuvadaki şampiyonun rol dışı cezası (0 = kendi rolü). Değer sunucudan (/api/champions), tablo motorda (§16.8):
 * istemci hesaplamaz, sadece gösterir.
 */
export function offRolePenalty(champions: Map<string, Champion>, champion: string | null, slot: Role): number {
  if (!champion) return 0
  return champions.get(champion)?.offRole[slot] ?? 0
}

/** Kalan adım sayısı (ilerleme göstergesi). */
export function stepIndex(draft: DraftState): number {
  return draft.actions.length
}
