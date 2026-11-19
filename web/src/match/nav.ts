// Haritada yürüme (UYGULAMA.md §8.26): iki nokta arası duvarlardan geçmeyen yol. Izgara görselden bir kez çıkarılır
// (npm run navgrid → navgrid.json, 5 birimlik hücreler). Yol: koridorda koridorun orta çizgisi, başka yerde A*.
import { laneSlice, MAP_SIZE, nearestOnLane, type Point } from './geometry'
import grid from './navgrid.json'
import type { Lane } from './types'

const CELL = grid.cell
const N = grid.size
const LANES: Lane[] = ['top', 'mid', 'bot']

/** Yürünebilir hücre: 1. */
const open = new Uint8Array(N * N)
grid.rows.forEach((row, y) => {
  for (let x = 0; x < N; x++) open[y * N + x] = row[x] === '.' ? 1 : 0
})

/**
 * Her hücrenin en yakın duvara uzaklığı (hücre). İki geçişli mesafe dönüşümü (komşu +1, çapraz +1,4).
 * A* bu uzaklıkla duvar dibini pahalı sayar: şampiyon yolun kenarından değil ortasından yürür.
 */
const clearance = (() => {
  const d = new Float32Array(N * N)
  for (let i = 0; i < N * N; i++) d[i] = open[i] ? 1e9 : 0
  const relax = (i: number, x: number, y: number, dx: number, dy: number, w: number) => {
    const nx = x + dx
    const ny = y + dy
    const v = nx < 0 || ny < 0 || nx >= N || ny >= N ? 0 : d[ny * N + nx]
    if (v + w < d[i]) d[i] = v + w
  }
  for (let y = 0; y < N; y++)
    for (let x = 0; x < N; x++) {
      const i = y * N + x
      if (!d[i]) continue
      relax(i, x, y, -1, 0, 1); relax(i, x, y, 0, -1, 1); relax(i, x, y, -1, -1, 1.4); relax(i, x, y, 1, -1, 1.4)
    }
  for (let y = N - 1; y >= 0; y--)
    for (let x = N - 1; x >= 0; x--) {
      const i = y * N + x
      if (!d[i]) continue
      relax(i, x, y, 1, 0, 1); relax(i, x, y, 0, 1, 1); relax(i, x, y, 1, 1, 1.4); relax(i, x, y, -1, 1, 1.4)
    }
  return d
})()

const cellOf = (p: Point) => [
  Math.max(0, Math.min(N - 1, Math.floor(p[0] / CELL))),
  Math.max(0, Math.min(N - 1, Math.floor(p[1] / CELL))),
]
const center = (i: number): Point => [((i % N) + 0.5) * CELL, (Math.floor(i / N) + 0.5) * CELL]

/** Nokta yürünebilir bir hücrede mi (harita dışı: hayır). */
export function walkable(p: Point): boolean {
  if (p[0] < 0 || p[1] < 0 || p[0] >= MAP_SIZE || p[1] >= MAP_SIZE) return false
  const [x, y] = cellOf(p)
  return open[y * N + x] === 1
}

/**
 * Durak noktası: duvara en az `margin` hücre uzaktaysa kendisi, değilse en yakın böyle hücrenin ortası.
 * Hedef noktalar (çukur çevresi dizilim, kuşatma) bazen duvara düşer; beklerken kıpırdanma da duvara taşmasın.
 */
export function settle(p: Point, margin = 2): Point {
  const [cx, cy] = cellOf(p)
  if (walkable(p) && clearance[cy * N + cx] >= margin) return p
  return center(nearestOpen(p, margin))
}

/** Noktaya en yakın, duvara en az `margin` hücre uzak hücre: halka halka arama. */
function nearestOpen(p: Point, margin = 1): number {
  const [cx, cy] = cellOf(p)
  for (let r = 0; r < N; r++) {
    let best = -1
    let bestD = Infinity
    for (let y = cy - r; y <= cy + r; y++)
      for (let x = cx - r; x <= cx + r; x++) {
        if (x < 0 || y < 0 || x >= N || y >= N || Math.max(Math.abs(x - cx), Math.abs(y - cy)) !== r) continue
        const i = y * N + x
        const d = Math.hypot(x - cx, y - cy)
        if (open[i] && clearance[i] >= margin && d < bestD) {
          best = i
          bestD = d
        }
      }
    if (best >= 0) return best
  }
  return cy * N + cx
}

const NEIGHBORS = [[1, 0, 1], [-1, 0, 1], [0, 1, 1], [0, -1, 1], [1, 1, 1.4142], [1, -1, 1.4142], [-1, 1, 1.4142], [-1, -1, 1.4142]] as const

/**
 * A*: adım bedeli = uzunluk × (1 + 2 / duvara uzaklık). Duvar dibi (uzaklık 1) ortadan (~4) iki kat pahalı:
 * yol ortaya çekilir ama kısa yoldan sapmaz. Çapraz adım iki duvarın arasından kayamaz.
 */
function astar(start: number, goal: number): number[] {
  const g = new Float64Array(N * N).fill(Infinity)
  const from = new Int32Array(N * N).fill(-1)
  const closed = new Uint8Array(N * N)
  const gx = goal % N
  const gy = Math.floor(goal / N)
  // Tahmin: kuş uçuşu × 1,5. Ortadan yürüme bedeli (≥ 1,25 kat) yüzünden saf kuş uçuşu çok iyimser, arama
  // bütün haritaya yayılırdı; 1,5 aramayı hedefe doğru daraltır (yol en kısadan en çok birkaç hücre sapar).
  const h = (i: number) => 1.5 * Math.hypot((i % N) - gx, Math.floor(i / N) - gy)
  // İkili yığın (öncelik = g + h).
  const heap: number[] = []
  const prio: number[] = []
  const push = (i: number, f: number) => {
    heap.push(i)
    prio.push(f)
    let k = heap.length - 1
    while (k > 0) {
      const parent = (k - 1) >> 1
      if (prio[parent] <= prio[k]) break
      ;[heap[parent], heap[k]] = [heap[k], heap[parent]]
      ;[prio[parent], prio[k]] = [prio[k], prio[parent]]
      k = parent
    }
  }
  const pop = () => {
    const top = heap[0]
    const lastI = heap.pop()!
    const lastF = prio.pop()!
    if (heap.length) {
      heap[0] = lastI
      prio[0] = lastF
      let k = 0
      for (;;) {
        const l = 2 * k + 1
        const r = l + 1
        let m = k
        if (l < heap.length && prio[l] < prio[m]) m = l
        if (r < heap.length && prio[r] < prio[m]) m = r
        if (m === k) break
        ;[heap[m], heap[k]] = [heap[k], heap[m]]
        ;[prio[m], prio[k]] = [prio[k], prio[m]]
        k = m
      }
    }
    return top
  }

  g[start] = 0
  push(start, h(start))
  while (heap.length) {
    const i = pop()
    if (i === goal) break
    if (closed[i]) continue
    closed[i] = 1
    const x = i % N
    const y = Math.floor(i / N)
    for (const [dx, dy, w] of NEIGHBORS) {
      const nx = x + dx
      const ny = y + dy
      if (nx < 0 || ny < 0 || nx >= N || ny >= N) continue
      const j = ny * N + nx
      if (!open[j] || closed[j]) continue
      if (dx && dy && (!open[y * N + nx] || !open[ny * N + x])) continue
      const cost = g[i] + w * (1 + 2 / Math.max(1, clearance[j]))
      if (cost < g[j]) {
        g[j] = cost
        from[j] = i
        push(j, cost + h(j))
      }
    }
  }
  const cells: number[] = []
  for (let i = goal; i !== -1; i = from[i]) cells.push(i)
  return cells.reverse()
}

/** Kırık çizgiyi yumuşat (Chaikin: her köşe iki kısa kesime bölünür) ve neredeyse doğrusal noktaları at. */
function smooth(points: Point[]): Point[] {
  let p = points
  for (let pass = 0; pass < 2 && p.length > 2; pass++) {
    const next: Point[] = [p[0]]
    for (let i = 0; i < p.length - 1; i++) {
      const [ax, ay] = p[i]
      const [bx, by] = p[i + 1]
      if (i > 0) next.push([ax * 0.75 + bx * 0.25, ay * 0.75 + by * 0.25])
      if (i < p.length - 2) next.push([ax * 0.25 + bx * 0.75, ay * 0.25 + by * 0.75])
    }
    next.push(p[p.length - 1])
    p = next
  }
  return simplify(p, 0.6)
}

/** Ramer-Douglas-Peucker: çizgiden `epsilon`'dan az sapan ara noktalar düşer (yol aynı, nokta az). */
function simplify(points: Point[], epsilon: number): Point[] {
  if (points.length < 3) return points
  const [ax, ay] = points[0]
  const [bx, by] = points[points.length - 1]
  const length = Math.hypot(bx - ax, by - ay) || 1
  let worst = 0
  let index = 0
  for (let i = 1; i < points.length - 1; i++) {
    const d = Math.abs((bx - ax) * (ay - points[i][1]) - (ax - points[i][0]) * (by - ay)) / length
    if (d > worst) {
      worst = d
      index = i
    }
  }
  if (worst <= epsilon) return [points[0], points[points.length - 1]]
  return [...simplify(points.slice(0, index + 1), epsilon).slice(0, -1), ...simplify(points.slice(index), epsilon)]
}

/** İki nokta da aynı koridorun orta çizgisine bu kadar yakınsa yol koridorun kendisi. */
const ON_LANE = 8

function laneRoute(from: Point, to: Point): Point[] | null {
  let best: { lane: Lane; worst: number; s0: number; s1: number } | null = null
  for (const lane of LANES) {
    const a = nearestOnLane(lane, from)
    const b = nearestOnLane(lane, to)
    const worst = Math.max(a.distance, b.distance)
    if (worst <= ON_LANE && (!best || worst < best.worst)) best = { lane, worst, s0: a.s, s1: b.s }
  }
  if (!best) return null
  const inner = laneSlice(best.lane, best.s0, best.s1).slice(1, -1)
  return [from, ...inner, to]
}

const cache = new Map<string, Point[]>()

/**
 * `from`'dan `to`'ya yürüme yolu (köşe noktaları, uçlar dahil). Aynı koridordaysa koridorun orta çizgisi;
 * değilse ızgarada A*, yumuşatılmış. Hücre çiftine göre önbellekte (orman kampları, üsse dönüşler tekrar eder).
 */
export function route(from: Point, to: Point): Point[] {
  const lane = laneRoute(from, to)
  if (lane) return lane
  const start = nearestOpen(from)
  const goal = nearestOpen(to)
  const key = `${start}:${goal}`
  let middle = cache.get(key)
  if (!middle) {
    middle = start === goal ? [] : smooth(astar(start, goal).map(center)).slice(1, -1)
    cache.set(key, middle)
  }
  return [from, ...middle, to]
}

/** Kırık çizginin uzunluğu. */
export function pathLength(points: readonly Point[]): number {
  let total = 0
  for (let i = 1; i < points.length; i++) total += Math.hypot(points[i][0] - points[i - 1][0], points[i][1] - points[i - 1][1])
  return total
}
