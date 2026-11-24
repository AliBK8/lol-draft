import { describe, expect, it } from 'vitest'
import { teamLabel, type SeriesHeader } from './seriesHeader'

describe('teamLabel', () => {
  const header: SeriesHeader = {
    format: 'bo3',
    fearless: false,
    need: 2,
    teams: { blue: { name: 'Kuzey Kurtları', wins: 1 }, red: { name: null, wins: 0 } },
  }

  it('takım adı varsa onu, yoksa renk adını yazar', () => {
    expect(teamLabel('blue', header)).toBe('Kuzey Kurtları')
    expect(teamLabel('red', header)).toBe('Kırmızı')
  })

  it('seri bilgisi yoksa (paylaşım sayfası) renk adı', () => {
    expect(teamLabel('blue')).toBe('Mavi')
  })
})
