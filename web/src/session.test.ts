import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import {
  clearRejoinToken,
  clearSession,
  loadRejoinToken,
  loadSession,
  LOBBY_LIFETIME_MS,
  saveSession,
} from './session'

// Node'da sessionStorage/localStorage yok: Map'li küçük bir sahte.
class MemoryStorage {
  private items = new Map<string, string>()
  get length() {
    return this.items.size
  }
  key = (index: number) => [...this.items.keys()][index] ?? null
  getItem = (key: string) => this.items.get(key) ?? null
  setItem = (key: string, value: string) => void this.items.set(key, value)
  removeItem = (key: string) => void this.items.delete(key)
}

const T0 = 1_800_000_000_000
const ticket = (code: string, rejoinToken = `r-${code}`) => ({ code, seat: 'B' as const, token: 't1', rejoinToken })

describe('session', () => {
  beforeEach(() => {
    ;(globalThis as { sessionStorage?: unknown }).sessionStorage = new MemoryStorage()
    ;(globalThis as { localStorage?: unknown }).localStorage = new MemoryStorage()
  })
  afterEach(() => {
    delete (globalThis as { sessionStorage?: unknown }).sessionStorage
    delete (globalThis as { localStorage?: unknown }).localStorage
  })

  it('lobi koduna göre saklar ve okur', () => {
    saveSession(ticket('AAA'))

    expect(loadSession('AAA')).toEqual({ seat: 'B', token: 't1' })
    expect(loadSession('BBB')).toBeNull()
  })

  it('siler', () => {
    saveSession(ticket('AAA'))
    clearSession('AAA')

    expect(loadSession('AAA')).toBeNull()
  })

  it('bozuk kaydı yok sayar', () => {
    globalThis.sessionStorage.setItem('lol-draft:lobby:AAA', '{"seat":"C","token":"x"}')

    expect(loadSession('AAA')).toBeNull()
  })

  it('depolama yoksa çökmez', () => {
    delete (globalThis as { sessionStorage?: unknown }).sessionStorage
    delete (globalThis as { localStorage?: unknown }).localStorage

    saveSession(ticket('AAA'))
    expect(loadSession('AAA')).toBeNull()
    expect(loadRejoinToken('AAA')).toBeNull()
  })

  // Koltuk anahtarı sekme kapanınca da kalır (localStorage); token kalmaz (sessionStorage).
  it('koltuk anahtarı sekme kapansa da kalır', () => {
    saveSession(ticket('AAA'), T0)
    ;(globalThis as { sessionStorage?: unknown }).sessionStorage = new MemoryStorage() // yeni sekme

    expect(loadSession('AAA')).toBeNull()
    expect(loadRejoinToken('AAA', T0 + 60_000)).toBe('r-AAA')
  })

  it('geri dönüşte yeni anahtar eskisinin yerine yazılır', () => {
    saveSession(ticket('AAA', 'eski'), T0)
    saveSession(ticket('AAA', 'yeni'), T0 + 1000)

    expect(loadRejoinToken('AAA', T0 + 2000)).toBe('yeni')
  })

  // Lobi en fazla 2 saat yaşar: daha eski anahtar okunmaz, yeni kayıtta da silinir.
  it('lobi ömründen eski anahtar okunmaz ve temizlenir', () => {
    saveSession(ticket('AAA'), T0)

    expect(loadRejoinToken('AAA', T0 + LOBBY_LIFETIME_MS - 1)).toBe('r-AAA')
    expect(loadRejoinToken('AAA', T0 + LOBBY_LIFETIME_MS)).toBeNull()

    saveSession(ticket('BBB'), T0 + LOBBY_LIFETIME_MS)
    expect(globalThis.localStorage.getItem('lol-draft:rejoin:AAA')).toBeNull()
    expect(loadRejoinToken('BBB', T0 + LOBBY_LIFETIME_MS)).toBe('r-BBB')
  })

  it('anahtar silinir, bozuk anahtar kaydı yok sayılır', () => {
    saveSession(ticket('AAA'), T0)
    clearRejoinToken('AAA')
    globalThis.localStorage.setItem('lol-draft:rejoin:BBB', '{"token":5}')

    expect(loadRejoinToken('AAA', T0)).toBeNull()
    expect(loadRejoinToken('BBB', T0)).toBeNull()
  })
})
