import { describe, expect, it } from 'vitest'
import fixture from './__fixtures__/match.json'
import { engagement } from './engagement'
import { nearestOnLane, pathPoint, TOWER_S } from './geometry'
import { placement, postsAt } from './positions'
import { roster, stateAt } from './timeline'
import type { Lane, MatchEvent, Replay } from './types'

// Gerçek bir maç (düzen olayı yok: harita düzeninden önceki kayıt). Testler araya düzen olayı ekler.
const replay = fixture as unknown as Replay

function withEvent(event: MatchEvent): Replay {
  const events = [...replay.events.filter((e) => e.t < event.t), event, ...replay.events.filter((e) => e.t >= event.t)]
  return { ...replay, events }
}

/** Takımın boşta (ölü değil, olayla meşgul değil) olduğu saniyelerde her şampiyonun hedef yeri. */
function idlePoints(r: Replay, side: 'blue' | 'red', from: number, to: number) {
  const out: { id: string; slot: number; t: number; point: readonly [number, number] }[] = []
  for (const [id, c] of roster(r)) {
    if (c.side !== side) continue
    for (let t = from; t <= to; t += 5) {
      const p = placement(r.events, stateAt(r, t), id, c.side, c.role, c.slot)
      if (p.kind === 'idle') out.push({ id, slot: c.slot, t, point: p.point })
    }
  }
  return out
}

describe('harita düzeni (DESIGN.md §4.11)', () => {
  it('düzen olayı yoksa eski kural: 14. dakikadan sonra bot ikilisi mid’de', () => {
    expect(postsAt(replay.events, 'blue', 20 * 60)).toBeUndefined()
    const points = idlePoints(replay, 'blue', 15 * 60, 18 * 60).filter((p) => p.slot === 3)
    expect(points.length).toBeGreaterThan(0)
    for (const p of points) expect(nearestOnLane('mid', p.point).distance, `@${p.t}`).toBeLessThan(25)
  })

  it('Koridorlar düzeninde bot ikilisi altta kalır, top üstte', () => {
    const lanes: Lane[] = ['top', 'mid', 'mid', 'bot', 'bot']
    const r = withEvent({ type: 'layout', t: 14 * 60, text: '', side: 'blue', layout: 'lanes', posts: ['top', null, 'mid', 'bot', 'bot'] })
    const points = idlePoints(r, 'blue', 15 * 60, 18 * 60).filter((p) => p.slot !== 1)
    expect(points.length).toBeGreaterThan(0)
    for (const p of points) expect(nearestOnLane(lanes[p.slot], p.point).distance, `${p.id} @${p.t}`).toBeLessThan(25)
  })

  it('Grup: ormancı dahil beşi aynı koridorda, üst üste binmeden', () => {
    const r = withEvent({ type: 'layout', t: 14 * 60, text: '', side: 'blue', layout: 'group', posts: ['bot', 'bot', 'bot', 'bot', 'bot'] })
    const points = idlePoints(r, 'blue', 15 * 60, 18 * 60)
    expect(new Set(points.map((p) => p.slot)).size).toBe(5)
    for (const p of points) expect(nearestOnLane('bot', p.point).distance, `${p.id} @${p.t}`).toBeLessThan(30)

    // Aynı saniyede iki ikon arasında en az yarım ikon (13) mesafe.
    const byT = new Map<number, (typeof points)[number][]>()
    for (const p of points) byT.set(p.t, [...(byT.get(p.t) ?? []), p])
    for (const [t, ps] of byT)
      for (let i = 0; i < ps.length; i++)
        for (let j = i + 1; j < ps.length; j++)
          expect(Math.hypot(ps[i].point[0] - ps[j].point[0], ps[i].point[1] - ps[j].point[1]), `@${t}`).toBeGreaterThan(13)
  })

  it('kuşatma koridoruyla gelirse savunanın o koridordaki inhibitör kulesinin önünde', () => {
    const t = 25 * 60 + 45
    const siege: MatchEvent = { type: 'siege', t, text: '', side: 'blue', lead: 20, tail: 4, lane: 'top' }
    const r = withEvent(siege)
    const [id, c] = [...roster(r)].find(([, x]) => x.side === 'blue')!
    const busy = engagement(r.events, t, id, c.side, c.slot)
    expect(busy?.kind).toBe('fight')
    const inhib = pathPoint('top', TOWER_S.red.top[2])
    const center = busy?.kind === 'fight' ? busy.center : ([0, 0] as const)
    expect(Math.hypot(center[0] - inhib[0], center[1] - inhib[1])).toBeLessThan(40)
  })
})
