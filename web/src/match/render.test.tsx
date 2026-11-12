import { renderToString } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { Champion } from '../api/types'
import fixture from './__fixtures__/match.json'
import { MapView } from './MapView'
import { MatchPlayer } from './MatchPlayer'
import { MatchReport } from './MatchReport'
import { placement } from './positions'
import { endTime, roster, stateAt } from './timeline'
import type { Replay } from './types'

// Tarayıcısız duman testi: bileşenler gerçek belgeyle çökmeden çizilir (efektler sunucu çiziminde çalışmaz).
const replay = fixture as unknown as Replay
const champions = new Map<string, Champion>(
  [...roster(replay).keys()].map((id) => [
    id,
    { id, name: id.toUpperCase(), roles: ['top'], offRole: { top: 0, jungle: 0, mid: 0, bot: 0, support: 0 } },
  ]),
)

describe('oynatıcı çizimi', () => {
  it('başlangıçta harita, 10 şampiyon ve 18 kule', () => {
    const html = renderToString(<MatchPlayer replay={replay} champions={champions} you="A" />)

    expect(html.match(/class="map-champ/g)).toHaveLength(10)
    expect(html.match(/class="map-tower/g)).toHaveLength(18)
    expect(html).toContain('00:00')
    // Karne sona kadar yok.
    expect(html).not.toContain('Altın kaynakları')
  })

  it('maç sonunda haritada yıkık kuleler ve işaretler', () => {
    const state = stateAt(replay, endTime(replay) - 5)
    const players = roster(replay)
    const placements = new Map(
      [...players].map(([id, c]) => [id, { side: c.side, placement: placement(replay.events, state, id, c.side, c.role, c.slot) }]),
    )
    const html = renderToString(<MapView state={state} events={replay.events} placements={placements} champions={champions} />)

    const lost = Object.values(state.towers.lost).flatMap((l) => Object.values(l)).reduce((a, b) => a + b, 0)
    expect(html.match(/map-tower (blue|red) down/g) ?? []).toHaveLength(lost)
  })

  it('karne: kazanan, altın tablosu, kurallar; koltuğa göre "kazandın/kaybettin"', () => {
    const winnerSeat = replay.end.winner === 'blue' ? replay.blueTeam : replay.blueTeam === 'A' ? 'B' : 'A'
    const loserSeat = winnerSeat === 'A' ? 'B' : 'A'

    const won = renderToString(<MatchReport replay={replay} champions={champions} you={winnerSeat} />)
    const lost = renderToString(<MatchReport replay={replay} champions={champions} you={loserSeat} />)
    const neutral = renderToString(<MatchReport replay={replay} champions={champions} />)

    expect(won).toContain('Kazandın!')
    expect(lost).toContain('Kaybettin.')
    expect(neutral).toContain('kazandı')
    expect(won).toContain('Altın kaynakları')
    expect(won).toContain(replay.report.blue.total.toLocaleString('tr-TR'))
    expect(won).toContain('Draft kuralları')
  })
})
