import type { Seat, Ticket } from './api/types'

// Misafir token'ı lobi koduna göre sessionStorage'da (DESIGN.md §8.17):
// - Sayfa yenilenince aynı sekme aynı koltuğa geri bağlanır (2 dk'lık hükmen sayacı durur).
// - Sekme kapanınca silinir; localStorage'da olsaydı başka sekme aynı koltuğu açar, iki bağlantı birbirini ezerdi.
// - Token 2 saat geçerli; süresi dolmuşsa sunucu 401 der, istemci kaydı siler.
// Gerçek "başka cihazdan geri dön" (rejoin) ertelendi.

const key = (code: string) => `lol-draft:lobby:${code}`

export interface Session {
  seat: Seat
  token: string
}

/** Tarayıcı dışı (test) ya da depolama kapalıysa (gizli mod ayarı) sessizce çalışmaz: oturum sadece bellekte kalır. */
function storage(): Storage | null {
  try {
    return globalThis.sessionStorage ?? null
  } catch {
    return null
  }
}

export function saveSession(ticket: Ticket): void {
  storage()?.setItem(key(ticket.code), JSON.stringify({ seat: ticket.seat, token: ticket.token }))
}

export function loadSession(code: string): Session | null {
  const raw = storage()?.getItem(key(code))
  if (!raw) return null
  try {
    const value = JSON.parse(raw) as Partial<Session>
    return value.token && (value.seat === 'A' || value.seat === 'B') ? { seat: value.seat, token: value.token } : null
  } catch {
    return null
  }
}

export function clearSession(code: string): void {
  storage()?.removeItem(key(code))
}
