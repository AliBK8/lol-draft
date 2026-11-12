// Skor gizliliği (karar 2026-11-12, DESIGN.md §8.15): sunucu sonucu hemen yazar ve yayınlar; istemci maçı izleyene
// (ya da "sona atla"ya) kadar göstermez. Lobi başına "kaç maçın sonucu açıldı" sessionStorage'da: sayfa yenilenince
// izlenmiş maç tekrar gizlenmez. 5b'de "sonucu göster" düğmesi açar, 5c'de oynatıcının bitişi.
import type { Game, LobbyState, Seat } from '../api/types'

const key = (code: string) => `lol-draft:revealed:${code}`

export function loadRevealed(code: string): number {
  try {
    return Number(globalThis.sessionStorage?.getItem(key(code)) ?? 0) || 0
  } catch {
    return 0
  }
}

export function saveRevealed(code: string, count: number): void {
  try {
    globalThis.sessionStorage?.setItem(key(code), String(count))
  } catch {
    // depolama kapalı: sadece bu sayfa ömrü boyunca hatırlanır
  }
}

export interface VisibleScore {
  winsA: number
  winsB: number
  /** Sonucu açılmış maçlar. */
  games: Game[]
  /** Seri bitti ve sonucu görülebilir (hükmen ise hemen; oynanarak bittiyse son maç izlenince). */
  winner: Seat | null
}

/** Ekranda gösterilecek skor: sadece açılmış maçlar sayılır. */
export function visibleScore(lobby: LobbyState, revealed: number): VisibleScore {
  const games = lobby.games.slice(0, revealed)
  const allSeen = revealed >= lobby.games.length
  return {
    winsA: games.filter((g) => g.winner === 'A').length,
    winsB: games.filter((g) => g.winner === 'B').length,
    games,
    winner: lobby.winner && (lobby.endReason === 'forfeit' || allSeen) ? lobby.winner : null,
  }
}
