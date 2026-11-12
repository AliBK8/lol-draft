import { describe, expect, it } from 'vitest'
import type { LobbyState } from '../api/types'
import { visibleScore } from './reveal'

const lobby = (extra: Partial<LobbyState>): LobbyState => ({
  code: 'AAA', revision: 1, format: 'bo3', fearless: false, isFull: true, gameNumber: 3, blueTeam: 'A',
  winsA: 2, winsB: 0, winner: 'A', endReason: 'played', ready: [], seats: [], draft: null,
  games: [
    { number: 1, blue: 'A', winner: 'A', match: 'm1' },
    { number: 2, blue: 'B', winner: 'A', match: 'm2' },
  ],
  serverNow: '2026-11-12T12:00:00Z', ...extra,
})

describe('gizli skor', () => {
  it('izlenmemiş maç sayılmaz, seri kazananı son maç izlenene kadar gizli', () => {
    expect(visibleScore(lobby({}), 0)).toMatchObject({ winsA: 0, winsB: 0, winner: null })
    expect(visibleScore(lobby({}), 1)).toMatchObject({ winsA: 1, winsB: 0, winner: null })
    expect(visibleScore(lobby({}), 2)).toMatchObject({ winsA: 2, winsB: 0, winner: 'A' })
  })

  it('hükmen galibiyet hemen görünür (izlenecek maç yok)', () => {
    const forfeit = lobby({ winner: 'B', endReason: 'forfeit', games: [], winsA: 0, winsB: 0 })

    expect(visibleScore(forfeit, 0).winner).toBe('B')
  })
})
