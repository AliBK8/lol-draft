// Harita geometrisi (DESIGN.md §7): 600×600, mavi üs sol alt, kırmızı sağ üst. Arka plan Riot'un Summoner's Rift
// görseli (Data Dragon map11.png, npm run icons); noktalar gerçek oyun koordinatlarından çevrilir.
import type { Side } from '../api/types'
import type { Lane, ObjectiveKind, River } from './types'

export type Point = readonly [number, number]

export const MAP_SIZE = 600

/** Yürüme hızı: LoL'de ~345 birim/sn; harita 14.990 birim = 600 → ~13,8 harita birimi/oyun sn. */
export const WALK_SPEED = (345 * MAP_SIZE) / 14990
export const MAP_IMAGE = '/map/map11.png'

// map11.png'nin kapsadığı Riot koordinatları (zone-painter ile aynı): x -120…14870, y -120…14980.
// Riot'ta y yukarı doğru artar, SVG'de aşağı: y ters çevrilir.
const MIN_X = -120
const MAX_X = 14870
const MIN_Y = -120
const MAX_Y = 14980

/** Riot oyun koordinatı → harita noktası. Örnek: ejder çukuru (9866, 4414) → (397, 420). */
export function riot(x: number, y: number): Point {
  return [((x - MIN_X) / (MAX_X - MIN_X)) * MAP_SIZE, MAP_SIZE - ((y - MIN_Y) / (MAX_Y - MIN_Y)) * MAP_SIZE]
}

/**
 * Kuleler: dış, iç, üs. Koridor boyunca yer gerçek veriden (1000 maçlık timeline'daki BUILDING_KILL konumları;
 * her kule her maçta aynı noktada). Gerçekte kule koridorun kenarında durur; burada şeridin ortasına alındı:
 * görselde koridora dik taranıp yol renkli şeridin orta noktası bulundu (genişlik ~38). Mid dış kuleler nehir
 * kavşağında (şerit geniş), mid'in orta çizgisine (x + y = 600) izdüşürüldü.
 */
export const TOWERS: Record<Side, Record<Lane, readonly [Point, Point, Point]>> = {
  blue: {
    top: [[50, 180], [50, 329], [52, 425]],
    mid: [[249, 351], [200, 397], [151, 448]],
    bot: [[425, 550], [282, 551], [176, 550]],
  },
  red: {
    top: [[178, 48], [323, 49], [424, 50]],
    mid: [[353, 247], [404, 201], [451, 151]],
    bot: [[550, 416], [550, 268], [549, 175]],
  },
}

/**
 * İnhibitörler: üs kulesinin arkasında (aynı yoldan: veri konumu, şeridin ortası). Motorda üs kulesiyle inhibitör
 * tek kademe (tier 2, log'da "inhibitörü"): o kademe düşünce ikisi birlikte yıkık görünür.
 */
export const INHIBITORS: Record<Side, Record<Lane, Point>> = {
  blue: { top: [51, 453], mid: [132, 466], bot: [144, 550] },
  red: { top: [456, 50], mid: [470, 133], bot: [549, 145] },
}

/** Üs (nexus önü): ölen buraya döner, recall buraya. */
export const BASE: Record<Side, Point> = { blue: [72, 528], red: [528, 72] }

/**
 * Koridor yolları mavi üsten kırmızı üsse (s = 0 mavi uç, s = 1 kırmızı uç). Şeridin ortasından, kulelerin ve
 * inhibitörlerin içinden geçer (şampiyonlar da ortada yürür); top ve bot haritanın kenarından, köşeden döner.
 * Köşe keskin değil, yuvarlatılmış bir yay (görseldeki koridor öyle; eski keskin köşe dışarıda küçük bir "kulak"
 * olarak kalır). Yay noktaları görselden ölçüldü: koridor piksellerinde mesafe dönüşümü (her pikselin kenara
 * uzaklığı), köşeden çıkan ışınlar boyunca en uzak nokta = şeridin ortası. Lane fazı bu yay civarında geçer.
 */
export const LANE_PATH: Record<Lane, Point[]> = {
  top: [
    BASE.blue, [51, 453], [52, 425], [50, 329], [50, 180],
    [49, 156], [54, 130], [64, 112], [74, 96], [85, 85], [96, 74], [112, 64], [130, 54], [156, 49],
    [178, 48], [323, 49], [424, 50], [456, 50], BASE.red,
  ],
  mid: [BASE.blue, [132, 466], [151, 448], [200, 397], [249, 351], [300, 300], [353, 247], [404, 201], [451, 151], [470, 133], BASE.red],
  bot: [
    BASE.blue, [144, 550], [176, 550], [282, 551], [425, 550],
    [440, 548], [466, 544], [485, 533], [502, 525], [512, 512], [525, 502], [534, 486], [544, 469], [548, 440],
    [550, 416], [550, 268], [549, 175], [549, 145], BASE.red,
  ],
}

/** Çukurlar (ELITE_MONSTER_KILL medyanı). Herald ve Baron aynı çukurda (üst), ejder ve Elder altta. */
export const PIT: Record<ObjectiveKind, Point> = {
  dragon: [399, 419],
  elder: [399, 419],
  herald: [203, 182],
  baron: [203, 182],
  finalFight: [300, 300],
}

/** Nehirdeki çatışma noktaları: mid ile çukur arası. */
export const RIVER_SPOT: Record<River, Point> = { top: [245, 238], bottom: [355, 362] }

/** Orman kampları (jungle.ts: ormancının programı). */
export type CampKind = 'blue' | 'wolves' | 'raptors' | 'red' | 'gromp' | 'krugs'
export const CAMP_KINDS: readonly CampKind[] = ['blue', 'wolves', 'raptors', 'red', 'gromp', 'krugs']

// Mavi tarafın kampları (Riot koordinatı); kırmızı taraf haritanın merkezine göre aynası: (14800 − x, 14900 − y).
const BLUE_CAMPS: Record<CampKind, readonly [number, number]> = {
  blue: [3800, 7900],
  wolves: [3800, 6500],
  raptors: [7000, 5400],
  red: [7700, 4000],
  gromp: [2100, 8400],
  krugs: [8400, 2700],
}

export const JUNGLE: Record<Side, Record<CampKind, Point>> = {
  blue: Object.fromEntries(CAMP_KINDS.map((k) => [k, riot(...BLUE_CAMPS[k])])) as Record<CampKind, Point>,
  red: Object.fromEntries(CAMP_KINDS.map((k) => [k, riot(14800 - BLUE_CAMPS[k][0], 14900 - BLUE_CAMPS[k][1])])) as Record<CampKind, Point>,
}

/** Kamplar liste olarak (mavi buff, kurtlar, kuşlar, kırmızı buff, gromp, kurbağa). */
export const CAMPS: Record<Side, Point[]> = {
  blue: CAMP_KINDS.map((k) => JUNGLE.blue[k]),
  red: CAMP_KINDS.map((k) => JUNGLE.red[k]),
}

const lengths = (path: readonly Point[]) =>
  path.slice(1).map((p, i) => Math.hypot(p[0] - path[i][0], p[1] - path[i][1]))

/** Koridor üzerinde s (0-1) noktası. */
export function pathPoint(lane: Lane, s: number): Point {
  const path = LANE_PATH[lane]
  const segments = lengths(path)
  let rest = Math.max(0, Math.min(1, s)) * segments.reduce((a, b) => a + b, 0)
  for (let i = 0; i < segments.length; i++) {
    if (rest <= segments[i] || i === segments.length - 1) {
      const f = segments[i] ? Math.min(1, rest / segments[i]) : 0
      return [path[i][0] + (path[i + 1][0] - path[i][0]) * f, path[i][1] + (path[i + 1][1] - path[i][1]) * f]
    }
    rest -= segments[i]
  }
  return path[0]
}

/** Noktanın koridor üzerindeki s değeri: yolun ona en yakın noktası (izdüşüm). */
export function sOf(lane: Lane, point: Point): number {
  return nearestOnLane(lane, point).s
}

/** Koridor yolunun noktaya en yakın yeri: s değeri ve yola uzaklık. */
export function nearestOnLane(lane: Lane, point: Point): { s: number; distance: number } {
  const path = LANE_PATH[lane]
  const segments = lengths(path)
  const total = segments.reduce((a, b) => a + b, 0)
  let best = { distance: Infinity, s: 0 }
  let walked = 0
  for (let i = 0; i < segments.length; i++) {
    const [ax, ay] = path[i]
    const [bx, by] = path[i + 1]
    const len2 = segments[i] ** 2 || 1
    const f = Math.max(0, Math.min(1, ((point[0] - ax) * (bx - ax) + (point[1] - ay) * (by - ay)) / len2))
    const distance = Math.hypot(ax + (bx - ax) * f - point[0], ay + (by - ay) * f - point[1])
    if (distance < best.distance) best = { distance, s: (walked + f * segments[i]) / total }
    walked += segments[i]
  }
  return best
}

/** Koridor yolunun s0'dan s1'e parçası (iki yöne de): uçlar dahil köşe noktaları. */
export function laneSlice(lane: Lane, s0: number, s1: number): Point[] {
  const path = LANE_PATH[lane]
  const segments = lengths(path)
  const total = segments.reduce((a, b) => a + b, 0)
  // Köşe noktalarının s değerleri; aradakiler parçaya girer.
  const corners: number[] = [0]
  for (const length of segments) corners.push(corners[corners.length - 1] + length / total)
  const lo = Math.min(s0, s1)
  const hi = Math.max(s0, s1)
  const inner = path.filter((_, i) => corners[i] > lo && corners[i] < hi)
  const points = [pathPoint(lane, lo), ...inner, pathPoint(lane, hi)]
  return s0 <= s1 ? points : points.reverse()
}

type Tiers = readonly [number, number, number]

const towerS = (side: Side, lane: Lane): Tiers => {
  const [outer, inner, base] = TOWERS[side][lane]
  return [sOf(lane, outer), sOf(lane, inner), sOf(lane, base)]
}

const sidesTowerS = (side: Side): Record<Lane, Tiers> => ({
  top: towerS(side, 'top'),
  mid: towerS(side, 'mid'),
  bot: towerS(side, 'bot'),
})

/** Kulelerin koridor üzerindeki yeri (s): dış, iç, üs. Gerçek kule noktalarından hesaplanır. */
export const TOWER_S: Record<Side, Record<Lane, Tiers>> = { blue: sidesTowerS('blue'), red: sidesTowerS('red') }
