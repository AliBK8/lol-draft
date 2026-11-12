// Şampiyonların haritadaki yeri (DESIGN.md §7, §8.19). Belgede koordinat yok: konum olaylardan ve rolden türetilir
// (prototipin kuralları). Öncelik sırası: ölü → üs; objektif → çukur; kuşatma → üs önü; hareket/split → koridor ya da
// nehir; recall → üs; yoksa rolün yeri. Saf fonksiyon: aynı T aynı konum (geri sarma tutarlı).
import type { Role, Side } from '../api/types'
import { BASE, CAMPS, pathPoint, PIT, RIVER_SPOT, TOWER_S, type Point } from './geometry'
import type { MatchState } from './timeline'
import type { Lane, MatchEvent } from './types'

export interface Placement {
  point: Point
  dead: boolean
}

const other = (side: Side): Side => (side === 'blue' ? 'red' : 'blue')

/**
 * Koridorun ön hattı (s, 0 mavi uç – 1 kırmızı uç). Kuleler düştükçe hat kaybeden tarafa doğru kayar:
 * mavi bir kule kaybettiyse ön hat mavi tarafa yaklaşır.
 */
export function laneFront(state: MatchState, lane: Lane): number {
  const lost = state.towers.lost
  const shift = (lost.red[lane] - lost.blue[lane]) * 0.09
  return Math.max(0.2, Math.min(0.8, 0.5 + shift))
}

/** Rolün koridoru (ormancı hariç). Geç oyunda dağılım değişir: bot ikilisi mid'e, top yan koridorda. */
function homeLane(role: Role, side: Side, minute: number): Lane {
  if (role === 'top') return minute >= 25 ? (side === 'blue' ? 'top' : 'bot') : 'top'
  if (role === 'mid') return 'mid'
  return minute >= 14 ? 'mid' : 'bot'
}

export function placement(
  events: readonly MatchEvent[],
  state: MatchState,
  champion: string,
  side: Side,
  role: Role,
  slot: number,
): Placement {
  const t = state.t
  const o = side === 'blue' ? 1 : -1 // mavi için +1: "kendi tarafına doğru" yönü
  const spread = slot - 2 // takım arkadaşları üst üste binmesin

  if (state.dead.has(champion)) {
    const [x, y] = BASE[side]
    return { point: [x + (slot % 3 - 1) * 14, y + (slot < 3 ? -8 : 10)], dead: true }
  }

  for (const e of events) {
    if (e.t > t + 25) break
    switch (e.type) {
      case 'objective':
        if (t >= e.t - 22 && t <= e.t + 5) {
          const [x, y] = PIT[e.objective]
          return at(x - o * 22 + spread * 7, y + o * 22 - spread * 7)
        }
        break
      case 'siege':
        if (t >= e.t - e.lead && t <= e.t + e.tail) {
          // Savunanın üssünün önünde; saldıran biraz daha dışarıda.
          const defender = other(e.side)
          const [bx, by] = BASE[defender]
          const d = defender === 'blue' ? 1 : -1
          const out = side === e.side ? 18 : -18
          return at(bx + d * (95 + out) + spread * 8, by - d * (95 + out) + spread * 6)
        }
        break
      case 'move':
        if (e.participants.includes(champion) && t >= e.t - e.lead && t <= e.t + e.tail) {
          if (e.river) {
            const [x, y] = RIVER_SPOT[e.river]
            return at(x - o * 14 + spread * 6, y + o * 14)
          }
          if (e.lane) {
            const [x, y] = pathPoint(e.lane, laneFront(state, e.lane) - o * 0.03)
            return at(x + 6, y + 6)
          }
        }
        break
      case 'split':
        if ((e.splitter === champion || e.answerer === champion) && t >= e.t - 20 && t <= e.t + 4) {
          // Split: rakibin kulesine doğru baskı; cevap veren karşısında.
          const s = TOWER_S[other(e.side)][0] + (e.side === 'blue' ? 0.04 : -0.04)
          const [x, y] = pathPoint(e.lane, s)
          return at(x + (e.splitter === champion ? 0 : 10), y + (e.splitter === champion ? 0 : 10))
        }
        break
    }
  }

  // Recall: oyuncu başına kaydırılmış periyotla üsse kısa dönüş.
  const offset = 60 * slot + (side === 'red' ? 25 : 0) + 90
  if (t > offset && (t - offset) % 270 < 14) {
    const [x, y] = BASE[side]
    return at(x + spread * 9, y - o * 4)
  }

  const minute = t / 60
  if (role === 'jungle') {
    if (minute >= 14 && Math.floor(t / 25) % 2 === 0) {
      const [x, y] = pathPoint('mid', laneFront(state, 'mid') - o * 0.09)
      return at(x - o * 20, y + o * 20)
    }
    return at(...CAMPS[side][Math.floor(t / 18) % CAMPS[side].length])
  }

  const lane = homeLane(role, side, minute)
  const back = role === 'support' ? 0.065 : role === 'bot' && minute >= 14 ? 0.05 : 0.035
  const [x, y] = pathPoint(lane, laneFront(state, lane) - o * back)
  // Mid'de toplanan ikili yan yana; lane fazında destek biraz arkada.
  const perp = lane === 'mid' && minute >= 14 ? ({ bot: 9, support: -9 } as Partial<Record<Role, number>>)[role] ?? 0 : 0
  return at(x + perp, y + perp)
}

const at = (x: number, y: number): Placement => ({ point: [x, y], dead: false })
