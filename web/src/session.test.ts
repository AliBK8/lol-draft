import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { clearSession, loadSession, saveSession } from './session'

// Node'da sessionStorage yok: Map'li küçük bir sahte.
class MemoryStorage {
  private items = new Map<string, string>()
  getItem = (key: string) => this.items.get(key) ?? null
  setItem = (key: string, value: string) => void this.items.set(key, value)
  removeItem = (key: string) => void this.items.delete(key)
}

describe('session', () => {
  beforeEach(() => {
    ;(globalThis as { sessionStorage?: unknown }).sessionStorage = new MemoryStorage()
  })
  afterEach(() => {
    delete (globalThis as { sessionStorage?: unknown }).sessionStorage
  })

  it('lobi koduna göre saklar ve okur', () => {
    saveSession({ code: 'AAA', seat: 'B', token: 't1' })

    expect(loadSession('AAA')).toEqual({ seat: 'B', token: 't1' })
    expect(loadSession('BBB')).toBeNull()
  })

  it('siler', () => {
    saveSession({ code: 'AAA', seat: 'A', token: 't1' })
    clearSession('AAA')

    expect(loadSession('AAA')).toBeNull()
  })

  it('bozuk kaydı yok sayar', () => {
    globalThis.sessionStorage.setItem('lol-draft:lobby:AAA', '{"seat":"C","token":"x"}')

    expect(loadSession('AAA')).toBeNull()
  })

  it('depolama yoksa çökmez', () => {
    delete (globalThis as { sessionStorage?: unknown }).sessionStorage

    saveSession({ code: 'AAA', seat: 'A', token: 't1' })
    expect(loadSession('AAA')).toBeNull()
  })
})
