import type { LobbyState } from '../api/types'

/**
 * Gelen durum elimdekinden yeni mi? (DESIGN.md §8.12) Yayınlar ağda yer değiştirebilir, yeniden bağlanınca da
 * eski bir durum gelebilir: küçük ya da eşit sürüm yok sayılır, ekran geri gitmez.
 */
export function newer(current: LobbyState | null, incoming: LobbyState): boolean {
  return current === null || incoming.revision > current.revision
}
