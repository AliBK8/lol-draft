import { describe, expect, it } from 'vitest'
import fixture from './__fixtures__/match.json'
import { BASE, PIT } from './geometry'
import { laneFront, placement } from './positions'
import { clock, endTime, goldDiffAt, roster, stateAt } from './timeline'
import type { Replay } from './types'

// Gerçek bir maç (iki geliştirme rakibinin oynadığı, sunucudan alınmış belge): 35. dk son savaş, kırmızı kazandı.
const replay = fixture as unknown as Replay

describe('zaman çizelgesi', () => {
  it('başta her şey sıfır, sonda belgedeki sonuç', () => {
    const start = stateAt(replay, 0)
    expect(start.kills).toEqual({ blue: 0, red: 0 })
    expect(start.log).toHaveLength(0)
    expect(start.finished).toBe(false)

    const end = stateAt(replay, endTime(replay))
    const kills = replay.events.filter((e) => e.type === 'kill')
    expect(end.kills.blue + end.kills.red).toBe(kills.length)
    expect(end.finished).toBe(true)
    // Log yeniden eskiye: ilk satır Nexus.
    expect(end.log[0].type).toBe('nexus')
  })

  it('log sadece metinli olaylar ve T anına kadar', () => {
    const t = 600
    const expected = replay.events.filter((e) => e.t <= t && e.text).length
    const state = stateAt(replay, t)

    expect(state.log).toHaveLength(expected)
    expect(state.log.every((e) => e.t <= t && e.text)).toBe(true)
  })

  it('ölen şampiyon yeniden doğana kadar ölü', () => {
    const kill = replay.events.find((e) => e.type === 'kill')!
    if (kill.type !== 'kill') throw new Error('kill bekleniyordu')

    expect(stateAt(replay, kill.t - 0.1).dead.has(kill.victim)).toBe(false)
    expect(stateAt(replay, kill.t).dead.has(kill.victim)).toBe(true)
    expect(stateAt(replay, kill.until).dead.has(kill.victim)).toBe(false)
  })

  it('kule: yıkan taraf değil, kaybeden sayılır', () => {
    const tower = replay.events.find((e) => e.type === 'tower')!
    if (tower.type !== 'tower') throw new Error('tower bekleniyordu')
    const loser = tower.side === 'blue' ? 'red' : 'blue'

    expect(stateAt(replay, tower.t).towers.lost[loser][tower.lane]).toBe(tower.tier + 1)
    expect(stateAt(replay, tower.t).towers.lost[tower.side][tower.lane]).toBe(0)
  })

  it('altın farkı dakika kareleri arasında doğrusal', () => {
    const diff = (minute: number) =>
      replay.goldFrames[minute].champions.reduce((s, c) => s + (c.side === 'blue' ? c.gold : -c.gold), 0)

    expect(goldDiffAt(replay, 600)).toBe(diff(10))
    expect(goldDiffAt(replay, 630)).toBe(Math.round((diff(10) + diff(11)) / 2))
  })

  it('kadro yuva sırasından: 5 mavi, 5 kırmızı, her rolden biri', () => {
    const r = roster(replay)
    expect(r.size).toBe(10)
    for (const side of ['blue', 'red'] as const) {
      expect([...r.values()].filter((c) => c.side === side).map((c) => c.role)).toEqual([
        'top', 'jungle', 'mid', 'bot', 'support',
      ])
    }
  })

  it('saat biçimi', () => {
    expect(clock(0)).toBe('00:00')
    expect(clock(2098.7)).toBe('34:58')
  })
})

describe('konumlar', () => {
  const r = roster(replay)

  it('ölü şampiyon kendi üssünde', () => {
    const kill = replay.events.find((e) => e.type === 'kill')!
    if (kill.type !== 'kill') throw new Error('kill bekleniyordu')
    const victim = r.get(kill.victim)!
    const p = placement(replay.events, stateAt(replay, kill.t + 1), kill.victim, victim.side, victim.role, victim.slot)

    expect(p.dead).toBe(true)
    expect(Math.hypot(p.point[0] - BASE[victim.side][0], p.point[1] - BASE[victim.side][1])).toBeLessThan(30)
  })

  it('objektif anında yaşayanlar çukurun yanında', () => {
    const objective = replay.events.find((e) => e.type === 'objective')!
    if (objective.type !== 'objective') throw new Error('objective bekleniyordu')
    const state = stateAt(replay, objective.t - 5)
    const pit = PIT[objective.objective]

    for (const [id, c] of r) {
      const p = placement(replay.events, state, id, c.side, c.role, c.slot)
      if (!p.dead) expect(Math.hypot(p.point[0] - pit[0], p.point[1] - pit[1])).toBeLessThan(60)
    }
  })

  it('kule kaybeden tarafa doğru ön hat kayar', () => {
    const base = stateAt(replay, 0)
    const blueLostTop = { ...base, towers: { lost: { blue: { top: 2, mid: 0, bot: 0 }, red: { top: 0, mid: 0, bot: 0 } } } }

    expect(laneFront(base, 'top')).toBe(0.5)
    expect(laneFront(blueLostTop, 'top')).toBeLessThan(0.5)
  })
})
