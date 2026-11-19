import { describe, expect, it } from 'vitest'
import fixture from './__fixtures__/match.json'
import { scoreboard, shortGold } from './scoreboard'
import { endTime, goldDiffAt, stateAt } from './timeline'
import type { Replay } from './types'

// Gerçek bir maç (match.test.ts ile aynı belge).
const replay = fixture as unknown as Replay

describe('skor tablosu', () => {
  it('maç sonunda kill ve ölümler olaylarla, takım altın farkı HUD ile aynı', () => {
    const state = stateAt(replay, endTime(replay))
    const board = scoreboard(replay, state)
    const kills = replay.events.filter((e) => e.type === 'kill')

    expect(board.champions).toHaveLength(10)
    expect(board.champions.reduce((s, c) => s + c.kills, 0)).toBe(kills.length)
    expect(board.champions.reduce((s, c) => s + c.deaths, 0)).toBe(kills.length)
    expect(board.teams.blue.kills + board.teams.red.kills).toBe(kills.length)
    // Şampiyon başına yuvarlama: en fazla 10 altın sapma.
    expect(Math.abs(board.teams.blue.gold - board.teams.red.gold - goldDiffAt(replay, state.t))).toBeLessThanOrEqual(10)
  })

  it('ölü şampiyonun yeniden doğmasına kalan süre; yaşayanınki yok', () => {
    const kill = replay.events.find((e) => e.type === 'kill')!
    if (kill.type !== 'kill') throw new Error('kill bekleniyordu')
    const board = scoreboard(replay, stateAt(replay, kill.t + 2))
    const victim = board.champions.find((c) => c.id === kill.victim)!

    expect(victim.respawnIn).toBe(Math.ceil(kill.until - kill.t - 2))
    expect(board.champions.find((c) => c.id === kill.killer)!.respawnIn).toBeNull()
  })

  it('canavarlar alan takıma; son savaş sayılmaz; kule yıkana', () => {
    const board = scoreboard(replay, stateAt(replay, endTime(replay)))
    const count = (side: string, kind: string) =>
      replay.events.filter((e) => e.type === 'objective' && e.side === side && e.objective === kind).length

    for (const side of ['blue', 'red'] as const) {
      expect(board.teams[side].objectives.dragon).toBe(count(side, 'dragon'))
      expect(board.teams[side].objectives.baron).toBe(count(side, 'baron'))
      expect(board.teams[side].towers).toBe(replay.events.filter((e) => e.type === 'tower' && e.side === side).length)
    }
  })

  it('kısa altın', () => {
    expect(shortGold(950)).toBe('950')
    expect(shortGold(8240)).toBe('8,2k')
    expect(shortGold(12000)).toBe('12k')
  })
})
