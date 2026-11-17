import type { Seat, Ticket } from './api/types'

// Misafir token'ı lobi koduna göre sessionStorage'da (DESIGN.md §8.17):
// - Sayfa yenilenince aynı sekme aynı koltuğa geri bağlanır (2 dk'lık hükmen sayacı durur).
// - Sekme kapanınca silinir; localStorage'da olsaydı başka sekme aynı koltuğu açar, iki bağlantı birbirini ezerdi.
// - Token 2 saat geçerli; süresi dolmuşsa sunucu 401 der, istemci kaydı siler.
//
// Koltuk anahtarı (rejoin, §8.24) ise localStorage'da: sekme kapanınca da kalır. Linki yeniden açan oyuncunun
// sekmesinde token yoksa anahtarla aynı koltuğu geri alır. Anahtar tek kullanımlık; her geri dönüşte yenisi yazılır.
// Lobi en fazla 2 saat yaşar: daha eski anahtar okunmaz, her kayıtta da silinir (localStorage'da birikmez).

const key = (code: string) => `lol-draft:lobby:${code}`
const rejoinPrefix = 'lol-draft:rejoin:'
const rejoinKey = (code: string) => `${rejoinPrefix}${code}`

/** Lobinin en uzun ömrü (sunucuda Lobby.MaxLifetime): bundan eski koltuk anahtarı işe yaramaz. */
export const LOBBY_LIFETIME_MS = 2 * 60 * 60 * 1000

export interface Session {
  seat: Seat
  token: string
}

interface StoredRejoin {
  token: string
  savedAt: number
}

/** Tarayıcı dışı (test) ya da depolama kapalıysa (gizli mod ayarı) sessizce çalışmaz: oturum sadece bellekte kalır. */
function storage(kind: 'sessionStorage' | 'localStorage'): Storage | null {
  try {
    return globalThis[kind] ?? null
  } catch {
    return null
  }
}

export function saveSession(ticket: Ticket, now = Date.now()): void {
  storage('sessionStorage')?.setItem(key(ticket.code), JSON.stringify({ seat: ticket.seat, token: ticket.token }))

  const local = storage('localStorage')
  if (!local) return
  pruneRejoinKeys(local, now)
  local.setItem(rejoinKey(ticket.code), JSON.stringify({ token: ticket.rejoinToken, savedAt: now } satisfies StoredRejoin))
}

export function loadSession(code: string): Session | null {
  const raw = storage('sessionStorage')?.getItem(key(code))
  if (!raw) return null
  try {
    const value = JSON.parse(raw) as Partial<Session>
    return value.token && (value.seat === 'A' || value.seat === 'B') ? { seat: value.seat, token: value.token } : null
  } catch {
    return null
  }
}

export function clearSession(code: string): void {
  storage('sessionStorage')?.removeItem(key(code))
}

/** Bu lobinin koltuk anahtarı; yoksa, bozuksa ya da lobi ömründen eskiyse null. */
export function loadRejoinToken(code: string, now = Date.now()): string | null {
  const stored = readRejoin(storage('localStorage')?.getItem(rejoinKey(code)) ?? null)
  return stored && now - stored.savedAt < LOBBY_LIFETIME_MS ? stored.token : null
}

export function clearRejoinToken(code: string): void {
  storage('localStorage')?.removeItem(rejoinKey(code))
}

function readRejoin(raw: string | null): StoredRejoin | null {
  if (!raw) return null
  try {
    const value = JSON.parse(raw) as Partial<StoredRejoin>
    return typeof value.token === 'string' && typeof value.savedAt === 'number'
      ? { token: value.token, savedAt: value.savedAt }
      : null
  } catch {
    return null
  }
}

// Süresi geçmiş (ya da bozuk) koltuk anahtarlarını siler: kapanmış lobilerin anahtarı birikmesin.
function pruneRejoinKeys(local: Storage, now: number): void {
  const stale: string[] = []
  for (let i = 0; i < local.length; i++) {
    const name = local.key(i)
    if (!name?.startsWith(rejoinPrefix)) continue
    const stored = readRejoin(local.getItem(name))
    if (!stored || now - stored.savedAt >= LOBBY_LIFETIME_MS) stale.push(name)
  }
  for (const name of stale) local.removeItem(name)
}
