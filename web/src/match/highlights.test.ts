import { describe, expect, it } from 'vitest'
import fixture from './__fixtures__/match.json'
import { announcementAt, timelineMarkers } from './highlights'
import { endTime } from './timeline'
import type { MatchEvent, Replay } from './types'

// Gerçek bir maç (match.test.ts ile aynı belge).
const replay = fixture as unknown as Replay
const events = replay.events

describe('duyuru bandı', () => {
  it('ilk kan: ilk kill penceresi boyunca, pencere geçince yok', () => {
    const first = events.find((e) => e.type === 'kill')!
    expect(first.text.startsWith('İlk kan')).toBe(true)

    // Bu maçta ilk ejder ilk kandan 2 sn önce: o an ejder duyurusu var, ilk kan değil.
    expect(announcementAt(events, first.t - 1, 40)?.title).not.toBe('İLK KAN')
    expect(announcementAt(events, first.t + 10, 40)?.title).toBe('İLK KAN')
    expect(announcementAt(events, first.t + 41, 40)?.title).not.toBe('İLK KAN')
  })

  it('sıradan kill duyurulmaz; ejder alan takımla duyurulur', () => {
    const second = events.filter((e) => e.type === 'kill')[1]
    const dragon = events.find((e) => e.type === 'objective' && e.objective === 'dragon')!
    if (dragon.type !== 'objective') throw new Error('objective bekleniyordu')

    // İkinci kill'den hemen sonra (pencere 1 sn) duyuru yok.
    expect(announcementAt(events, second.t, 1)).toBeNull()
    const a = announcementAt(events, dragon.t + 1, 40)!
    expect(a.title).toBe('EJDER')
    expect(a.side).toBe(dragon.side)
    expect(a.icon).toBe('dragon')
  })

  it('istila: çalınırsa istila edenin, püskürtülürse savunanın renginde', () => {
    const invade = (stolen: boolean): MatchEvent => ({
      type: 'invade', t: 250, text: 'İstila', side: 'blue', camp: 'krugs', invaders: ['a'], defenders: stolen ? [] : ['b'],
      stolen, probability: stolen ? null : 0.4, lead: 16, tail: 4,
    })
    const stolen = announcementAt([invade(true)], 251, 40)!
    expect([stolen.title, stolen.side]).toEqual(['ORMAN İSTİLASI', 'blue'])
    const repelled = announcementAt([invade(false)], 251, 40)!
    expect([repelled.title, repelled.side]).toEqual(['İSTİLA PÜSKÜRTÜLDÜ', 'red'])
  })

  it('maç sonunda nexus', () => {
    expect(announcementAt(events, endTime(replay), 40)?.title).toBe('NEXUS DÜŞTÜ')
  })
})

describe('zaman çizelgesi', () => {
  it('her kill, canavar, kule ve nexus için bir işaret; hepsi 0-1 arasında', () => {
    const markers = timelineMarkers(events, endTime(replay))
    const expected = events.filter(
      (e) => e.type === 'kill' || e.type === 'tower' || e.type === 'nexus' || (e.type === 'objective' && e.objective !== 'finalFight'),
    ).length

    expect(markers).toHaveLength(expected)
    expect(markers.every((m) => m.at >= 0 && m.at <= 1)).toBe(true)
    expect(markers.filter((m) => m.kind === 'kill')).toHaveLength(events.filter((e) => e.type === 'kill').length)
  })
})
