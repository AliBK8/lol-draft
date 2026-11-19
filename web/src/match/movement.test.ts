import { describe, expect, it } from 'vitest'
import fixture from './__fixtures__/match.json'
import { BASE, CAMPS, PIT, type Point } from './geometry'
import { buildTracks, MAX_BOOST, poseAt, WALK_SPEED } from './movement'
import { route, walkable } from './nav'
import { endTime, roster, stateAt } from './timeline'
import type { Replay } from './types'

// Gerçek bir maç (match.test.ts ile aynı belge).
const replay = fixture as unknown as Replay
const tracks = buildTracks(replay)
const players = roster(replay)
const end = endTime(replay)
const distance = (a: Point, b: Point) => Math.hypot(a[0] - b[0], a[1] - b[1])

describe('yürüme yolu', () => {
  it('duvardan geçmez: yol boyunca her birim yürünebilir', () => {
    const pairs: [Point, Point][] = [
      [BASE.blue, PIT.baron],
      [CAMPS.blue[0], CAMPS.blue[3]],
      [CAMPS.red[1], PIT.dragon],
      [BASE.red, BASE.blue],
    ]
    for (const [from, to] of pairs) {
      const path = route(from, to)
      for (let i = 1; i < path.length; i++) {
        const steps = Math.ceil(distance(path[i - 1], path[i]))
        for (let k = 0; k <= steps; k++) {
          const f = k / Math.max(1, steps)
          const p: Point = [path[i - 1][0] + (path[i][0] - path[i - 1][0]) * f, path[i - 1][1] + (path[i][1] - path[i - 1][1]) * f]
          expect(walkable(p), `${from} → ${to}: ${p}`).toBe(true)
        }
      }
    }
  })
})

describe('hareket', () => {
  it('hiçbir an duvarda değil', () => {
    for (const [id, track] of tracks) {
      for (let t = 0; t <= end; t += 1) expect(walkable(poseAt(track, t).point), `${id} @ ${t}`).toBe(true)
    }
  })

  it('ışınlanma yok: ardışık anlar arası en fazla koşma hızı; tek istisna recall ve doğma (kendi üssüne)', () => {
    const dt = 0.5
    for (const [id, track] of tracks) {
      const side = players.get(id)!.side
      let previous = poseAt(track, 0)
      for (let t = dt; t <= end; t += dt) {
        const pose = poseAt(track, t)
        // Kıpırdanma payı 2 birim.
        if (!previous.dead && !pose.dead && distance(previous.point, pose.point) > WALK_SPEED * MAX_BOOST * dt + 2) {
          expect(distance(pose.point, BASE[side]), `${id} @ ${t}`).toBeLessThan(40)
        }
        previous = pose
      }
    }
  })

  it('objektif anında yaşayanlar çukurda (zamanında yola çıktılar)', () => {
    const objectives = replay.events.filter((e) => e.type === 'objective')
    expect(objectives.length).toBeGreaterThan(0)
    for (const e of objectives) {
      if (e.type !== 'objective') continue
      const state = stateAt(replay, e.t)
      for (const [id, track] of tracks) {
        if (state.dead.has(id)) continue
        expect(distance(poseAt(track, e.t).point, PIT[e.objective]), `${id} @ ${e.objective} ${e.t}`).toBeLessThan(60)
      }
    }
  })

  it('ölen öldüğü yerde silik kalır, doğunca kendi üssünde belirir', () => {
    const kill = replay.events.find((e) => e.type === 'kill')!
    if (kill.type !== 'kill') throw new Error('kill bekleniyordu')
    const track = tracks.get(kill.victim)!
    const side = players.get(kill.victim)!.side
    const before = poseAt(track, Math.ceil(kill.t) - 0.01)
    const dead = poseAt(track, kill.until - 1)

    expect(dead.dead).toBe(true)
    expect(distance(dead.point, before.point)).toBeLessThan(1)
    const reborn = poseAt(track, kill.until + 1)
    expect(reborn.dead).toBe(false)
    expect(distance(reborn.point, BASE[side])).toBeLessThan(40)
  })

  it('saf: aynı T aynı konum, güzergâh her seferinde aynı', () => {
    const again = buildTracks(replay)
    for (const [id, track] of tracks) {
      for (const t of [0, 123.4, 600, end]) expect(poseAt(again.get(id)!, t)).toEqual(poseAt(track, t))
    }
  })
})
