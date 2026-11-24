// Şampiyonların haritadaki hedef yeri (DESIGN.md §7, §8.19): "şu an nerede olmalı". Oraya yürüyüş movement.ts'de
// (§8.26). Belgede koordinat yok: hedef olaylardan ve rolden türetilir (prototipin kuralları). Öncelik sırası:
// ölü → üs; objektif → çukur; kuşatma → üs önü; hareket/split → koridor ya da nehir; recall → üs; yoksa rolün yeri.
// Saf fonksiyon: aynı T aynı hedef (geri sarma tutarlı).
import type { Role, Side } from '../api/types'
import { engagement } from './engagement'
import { BASE, JUNGLE, pathPoint, RIVER_SPOT, TOWER_S, type Point } from './geometry'
import { jungleTarget } from './jungle'
import { formation } from './nav'
import type { MatchState } from './timeline'
import type { Lane, MatchEvent } from './types'

/** Konumun nedeni: hareket katmanı (movement.ts) buna göre yürür, bekler ya da ışınlanır. */
export type PlacementKind = 'dead' | 'event' | 'recall' | 'idle'

export interface Placement {
  point: Point
  dead: boolean
  kind: PlacementKind
  /** Olay hedefinde en geç varış anı (olayın saniyesi); yetişemeyen hızlanır. */
  deadline?: number
}

const other = (side: Side): Side => (side === 'blue' ? 'red' : 'blue')

/**
 * Savaş dizilişi: her takım olay noktasının kendi tarafında, iki sıra. Ön sıra (top, ormancı, support) olaya yakın,
 * arka sıra (mid, adc) geride. Yuva → [ileri, yan] (birim). Savaş anında (clash) ön sıra merkeze girer.
 * Yan eksen iki takımda aynı: top topa, support supporta bakar.
 */
const ROWS: Record<'ready' | 'clash', readonly (readonly [number, number])[]> = {
  //       top        jungle    mid        bot        support
  ready: [[28, -30], [28, 0], [56, -15], [56, 15], [28, 30]],
  clash: [[13, -30], [13, 0], [40, -15], [40, 15], [13, 30]],
}
const FORMATION_GAP = 26 // ikon çapı 26: kenar kenara

const SIDES: Side[] = ['blue', 'red']
// 8 yön (45°'lik): dizilişin ekseni bunlardan seçilir.
const DIRECTIONS: Point[] = Array.from({ length: 8 }, (_, k) => [Math.cos((k * Math.PI) / 4), Math.sin((k * Math.PI) / 4)])

type Phase = 'ready' | 'clash'

/** Eksen boyunca istenen yerler: f mavinin "geri" yönü (kırmızınınki −f), yan eksen f'nin 90° döndürülmüşü. */
function desiredSpots(c: Point, f: Point, phase: Phase): { key: string; want: Point }[] {
  const l: Point = [-f[1], f[0]]
  return [1, 0, 4, 2, 3].flatMap((s) =>
    SIDES.map((sd) => {
      const o = sd === 'blue' ? 1 : -1
      const [ahead, lateral] = ROWS[phase][s]
      return {
        key: `${sd}${s}`,
        want: [c[0] + o * ahead * f[0] + lateral * l[0], c[1] + o * ahead * f[1] + lateral * l[1]] as Point,
      }
    }),
  )
}

/**
 * Yerler önce bulunur, kime düştüğü sonra dağıtılır: duvar yüzünden bir takımın yerleri öbür yana taşabilir.
 * Eksende maviye en yakın 5 yer mavinin; takımda cepheye en yakın 3'ü ön sıra. Sıralar kendi içinde yan eksene
 * göre: ön sıra top → ormancı → support, arka sıra mid → adc.
 */
function assign(c: Point, f: Point, placed: readonly Point[]): Map<string, Point> {
  const ahead = (p: Point) => (p[0] - c[0]) * f[0] + (p[1] - c[1]) * f[1] // + mavi tarafı
  const lateral = (p: Point) => -(p[0] - c[0]) * f[1] + (p[1] - c[1]) * f[0]
  const byAhead = [...placed].sort((a, b) => ahead(b) - ahead(a))
  const spots = new Map<string, Point>()
  for (const [sd, team] of [['blue', byAhead.slice(0, 5)], ['red', byAhead.slice(5)]] as const) {
    const toFront = [...team].sort((a, b) => Math.abs(ahead(a)) - Math.abs(ahead(b)))
    const front = toFront.slice(0, 3).sort((a, b) => lateral(a) - lateral(b))
    const back = toFront.slice(3).sort((a, b) => lateral(a) - lateral(b))
    const bySlot: [number, Point][] = [[0, front[0]], [1, front[1]], [4, front[2]], [2, back[0]], [3, back[1]]]
    for (const [s, p] of bySlot) spots.set(`${sd}${s}`, p)
  }
  return spots
}

const axes = new Map<string, Point>()
const formations = new Map<string, Map<string, Point>>()

/**
 * Dizilişin ekseni (merkez başına bir kez): mavi üsse bakan yönler denenir, istenen yerlerden toplam kayması en
 * az olan seçilir. Açık alanda (son savaş) çapraz, ejder çukurunda nehir boyunca çıkar: çukurun mavi yanı duvar.
 */
function axisOf(c: Point): Point {
  const key = `${c[0]},${c[1]}`
  let best = axes.get(key)
  if (best) return best
  const toBlue: Point = [BASE.blue[0] - c[0], BASE.blue[1] - c[1]]
  let bestCost = Infinity
  for (const f of DIRECTIONS) {
    if (f[0] * toBlue[0] + f[1] * toBlue[1] <= 1e-6) continue
    const want = desiredSpots(c, f, 'ready').map((d) => d.want)
    const placed = formation(c, want, FORMATION_GAP)
    const cost = placed.reduce((sum, p, i) => sum + Math.hypot(p[0] - want[i][0], p[1] - want[i][1]), 0)
    if (cost < bestCost - 1e-6) {
      bestCost = cost
      best = f
    }
  }
  axes.set(key, best!)
  return best!
}

/** Merkezin dizilişi (10 yuva; katılmayanların yeri boş kalır). Merkez ve evre başına bir kez: geri sarma tutarlı. */
function formationSpot(c: Point, phase: Phase, side: Side, slot: number): Point {
  const key = `${c[0]},${c[1]},${phase}`
  let spots = formations.get(key)
  if (!spots) {
    const f = axisOf(c)
    spots = assign(c, f, formation(c, desiredSpots(c, f, phase).map((d) => d.want), FORMATION_GAP))
    formations.set(key, spots)
  }
  return spots.get(`${side}${slot}`)!
}

/** Olay dizilişindeki yer: olaydan önce bekleme sırası, olay anından sonra çarpışma. */
const fight = (e: { t: number }, t: number, centerPoint: Point, side: Side, slot: number): Placement =>
  event(e.t, ...formationSpot(centerPoint, t >= e.t ? 'clash' : 'ready', side, slot))

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
    return { point: [x + (slot % 3 - 1) * 14, y + (slot < 3 ? -8 : 10)], dead: true, kind: 'dead' }
  }

  const busy = engagement(events, t, champion, side, slot)
  switch (busy?.kind) {
    case 'fight':
      return fight(busy, t, busy.center, side, slot)
    case 'lane':
      // Koridor boyunca kendi tarafına biraz geride (yana kaydırmak şeridin kenarına iterdi).
      return event(busy.t, ...pathPoint(busy.lane, laneFront(state, busy.lane) - o * 0.04))
    case 'split': {
      // Split: rakibin kulesine doğru baskı; cevap veren karşısında, koridor boyunca kendi tarafında.
      const s = TOWER_S[other(busy.attacker)][busy.lane][0] + (busy.attacker === 'blue' ? 0.04 : -0.04)
      return event(busy.t, ...pathPoint(busy.lane, busy.splitter ? s : s - o * 0.012))
    }
    case 'recall': {
      const [x, y] = BASE[side]
      return { point: [x + spread * 9, y - o * 4], dead: false, kind: 'recall' }
    }
  }

  const minute = t / 60
  if (role === 'jungle') {
    const target = jungleTarget(events, champion, side, slot, t)
    switch (target.kind) {
      case 'base': {
        const [x, y] = BASE[side]
        return at(x + spread * 9, y - o * 4)
      }
      case 'camp':
        return at(...JUNGLE[side][target.camp])
      case 'scuttle': {
        // Yengeç: nehirde, kendi tarafına doğru (rakip ormancıyla aynı noktada dikilmesin).
        const [x, y] = RIVER_SPOT[target.river]
        return at(x - o * 18, y + o * 18)
      }
      case 'hover': {
        const [x, y] = pathPoint('mid', laneFront(state, 'mid') - o * 0.09)
        return at(x - o * 20, y + o * 20)
      }
    }
  }

  const lane = homeLane(role, side, minute)
  const back = role === 'support' ? 0.065 : role === 'bot' && minute >= 14 ? 0.05 : 0.035
  const [x, y] = pathPoint(lane, laneFront(state, lane) - o * back)
  // Mid'de toplanan ikili yan yana; lane fazında destek biraz arkada.
  const perp = lane === 'mid' && minute >= 14 ? ({ bot: 9, support: -9 } as Partial<Record<Role, number>>)[role] ?? 0 : 0
  return at(x + perp, y + perp)
}

const at = (x: number, y: number): Placement => ({ point: [x, y], dead: false, kind: 'idle' })
const event = (deadline: number, x: number, y: number): Placement => ({ point: [x, y], dead: false, kind: 'event', deadline })
