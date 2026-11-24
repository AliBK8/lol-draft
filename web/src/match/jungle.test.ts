import { describe, expect, it } from 'vitest'
import fixture from './__fixtures__/match.json'
import { deadAt, engagement } from './engagement'
import { CAMP_KINDS, JUNGLE } from './geometry'
import { jungleTarget, type JungleTarget } from './jungle'
import { buildTracks, poseAt } from './movement'
import { walkable } from './nav'
import { roster } from './timeline'
import type { MatchEvent, Replay } from './types'

// Gerçek bir maç (match.test.ts ile aynı belge).
const replay = fixture as unknown as Replay
const junglers = [...roster(replay)].filter(([, c]) => c.role === 'jungle')
const label = (j: JungleTarget) => (j.kind === 'camp' ? j.camp : j.kind)

/** Ormancının olay dışındaki (boşta) hedefleri, saniye saniye. */
function idleTargets(id: string, from: number, to: number): { t: number; target: JungleTarget }[] {
  const c = roster(replay).get(id)!
  const out: { t: number; target: JungleTarget }[] = []
  for (let t = from; t <= to; t++) {
    if (deadAt(replay.events, t, id) || engagement(replay.events, t, id, c.side, c.slot)) continue
    out.push({ t, target: jungleTarget(replay.events, id, c.side, c.slot, t) })
  }
  return out
}

describe('ormancının programı', () => {
  it('iki ormancı birbirinin kopyası değil: kamp değiştirme anları farklı', () => {
    const changes = junglers.map(([id]) => {
      const list = idleTargets(id, 0, 600)
      return list.filter((x, i) => i > 0 && label(x.target) !== label(list[i - 1].target)).map((x) => x.t)
    })
    const shared = changes[0].filter((t) => changes[1].includes(t))
    expect(changes[0].length).toBeGreaterThan(5)
    expect(shared.length / changes[0].length).toBeLessThan(0.3)
  })

  it('ilk turda altı kampın hepsine uğrar (4:30 içinde)', () => {
    for (const [id] of junglers) {
      const seen = new Set(idleTargets(id, 0, 270).map((x) => label(x.target)))
      for (const camp of CAMP_KINDS) expect(seen, `${id}: ${camp}`).toContain(camp)
    }
  })

  it('istila: iki ormancı rakibin kampına yürür, olay anında orada; duvara girmez, orman programı beklemede', () => {
    const [[blueId, blue], [redId]] = junglers[0][1].side === 'blue' ? junglers : [junglers[1], junglers[0]]
    const invade: MatchEvent = {
      type: 'invade', t: 250, text: 'İstila', side: blue.side, camp: 'raptors', invaders: [blueId], defenders: [redId],
      stolen: true, probability: 0.6, lead: 16, tail: 4,
    }
    const events = [...replay.events.filter((e) => e.t < 250), invade, ...replay.events.filter((e) => e.t >= 250)]
    const withInvade: Replay = { ...replay, events }
    const tracks = buildTracks(withInvade)
    const camp = JUNGLE.red.raptors

    for (const id of [blueId, redId]) {
      const pose = poseAt(tracks.get(id)!, 250)
      expect(Math.hypot(pose.point[0] - camp[0], pose.point[1] - camp[1]), id).toBeLessThan(60)
      for (let t = 200; t <= 270; t++) expect(walkable(poseAt(tracks.get(id)!, t).point), `${id} @ ${t}`).toBe(true)
      expect(engagement(events, 245, id, roster(withInvade).get(id)!.side, 1)?.kind).toBe('fight')
    }
    // İki ormancı üst üste değil.
    const [a, b] = [poseAt(tracks.get(blueId)!, 250).point, poseAt(tracks.get(redId)!, 250).point]
    expect(Math.hypot(a[0] - b[0], a[1] - b[1])).toBeGreaterThan(20)
  })

  it('maç başında üste, yengeç 3:30 doğmadan nehre gitmez', () => {
    for (const [id] of junglers) {
      const list = idleTargets(id, 0, 600)
      expect(label(list[0].target)).toBe('base')
      for (const x of list) if (x.target.kind === 'scuttle') expect(x.t, id).toBeGreaterThanOrEqual(210)
      for (const x of list) expect(x.target.kind, `${id} @ ${x.t}`).not.toBe('hover')
    }
  })
})
