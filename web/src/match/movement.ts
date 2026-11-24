// Şampiyon hareketi (UYGULAMA.md §8.26): şampiyonlar hedeflerine ışınlanmaz, haritada yürür.
//
// 1. Hedefler: positions.ts her saniye "şu an nerede olmalı" der (olay, recall, ölüm, rolün yeri). Aynı hedefin
//    ardışık saniyeleri bir "durak" olur: [başlangıç, bitiş], nokta, tür, olaysa en geç varış anı.
// 2. Güzergâh: duraklar sırayla yürünür (nav.ts: duvardan geçmeyen yol, sabit hız). Olay durağına zamanında
//    varmak için yola erken çıkılır (başlangıç − yol süresi); yetişemeyen en fazla 2 kat hızlanır.
//    Ölüm: yerinde silik kalır, üste doğar. Recall: 8 sn kanal, üsse ışınlanır (oyundaki gibi), sonra yürür.
// 3. Konum: maç başına bir kez hesaplanan güzergâhta T anının noktası. Saf: aynı T aynı konum, geri sarma bedava.
//
// Sadece bilinen olaylara bakar (karar anları gelince yeni olaylar eklenir, güzergâh o andan yeniden kurulur).
import type { Side } from '../api/types'
import { BASE, WALK_SPEED, type Point } from './geometry'
import { pathLength, route, settle } from './nav'
import { placement, type PlacementKind } from './positions'
import { endTime, roster, stateAt, type MatchState } from './timeline'
import type { Replay } from './types'

/** Haritada çizilen an: nokta ve ölü mü. */
export interface Pose {
  point: Point
  dead: boolean
}

// Yürüme hızı geometry.ts'te (ormancının programı da kullanır); testler buradan alır.
export { WALK_SPEED }
/** Olaya yetişemeyen bu kata kadar hızlanır (koşma, hayalet, hız iksiri). */
export const MAX_BOOST = 2
/** Recall kanalı (oyun sn): bu süre yerinde durur, sonra üsse ışınlanır. */
export const RECALL_CHANNEL = 8
/** Hedef örnekleme aralığı (oyun sn). */
const STEP = 1

interface Station {
  kind: PlacementKind
  start: number
  end: number
  point: Point
  deadline?: number
}

/** Güzergâhın bir parçası: t0'da yola çıkar, t1'de varır (t0 = t1: o noktada beklemeye başlar ya da ışınlanır). */
interface Leg {
  t0: number
  t1: number
  points: Point[]
  /** Noktaların yol başından uzaklığı. */
  cum: number[]
  dead: boolean
}

export interface Track {
  legs: Leg[]
  /** Beklerken hafif kıpırdanmanın evresi (şampiyonlar aynı anda aynı yöne sallanmasın). */
  phase: number
}

function leg(t0: number, t1: number, points: Point[], dead = false): Leg {
  const cum = [0]
  for (let i = 1; i < points.length; i++)
    cum.push(cum[i - 1] + Math.hypot(points[i][0] - points[i - 1][0], points[i][1] - points[i - 1][1]))
  return { t0, t1, points, cum, dead }
}

/** Parçanın t anındaki noktası: yolda geçen sürenin oranı kadar yol alınmış. */
function pointOn(l: Leg, t: number): Point {
  const total = l.cum[l.cum.length - 1]
  if (t >= l.t1 || l.t1 <= l.t0 || total === 0) return l.points[l.points.length - 1]
  const d = (Math.max(0, t - l.t0) / (l.t1 - l.t0)) * total
  let i = 1
  while (i < l.cum.length - 1 && l.cum[i] < d) i++
  const f = (d - l.cum[i - 1]) / (l.cum[i] - l.cum[i - 1] || 1)
  const [ax, ay] = l.points[i - 1]
  const [bx, by] = l.points[i]
  return [ax + (bx - ax) * f, ay + (by - ay) * f]
}

/** Parçayı t anında kes: o ana kadar yürünen kısım kalır. */
function truncate(l: Leg, t: number): Leg {
  if (t >= l.t1) return l
  const here = pointOn(l, t)
  const total = l.cum[l.cum.length - 1]
  const d = ((t - l.t0) / (l.t1 - l.t0)) * total
  const kept = l.points.filter((_, i) => l.cum[i] < d)
  return leg(l.t0, t, [...kept, here], l.dead)
}

/** Hedeflerin durak listesi: her saniyenin hedefi, aynı hedef sürdükçe aynı durak. */
function stationsOf(replay: Replay, states: readonly MatchState[], id: string, side: Side, role: Parameters<typeof placement>[4], slot: number): Station[] {
  const list: Station[] = []
  states.forEach((state) => {
    const p = placement(replay.events, state, id, side, role, slot)
    // Ölü hedefi (üs) çizilmez, sadece doğma yeri; diğerleri duvardan uzak bir yere oturur.
    const point = p.kind === 'dead' ? p.point : settle(p.point)
    const last = list[list.length - 1]
    if (
      last &&
      last.kind === p.kind &&
      last.deadline === p.deadline &&
      Math.hypot(last.point[0] - point[0], last.point[1] - point[1]) < 1.5
    ) {
      last.end = state.t + STEP
    } else {
      list.push({ kind: p.kind, start: state.t, end: state.t + STEP, point, deadline: p.deadline })
    }
  })
  return list
}

/** Durak listesinden güzergâh. `floor`: bundan önce yola çıkılamaz (olay sürüyor, ışınlanma/doğma anı). */
function buildTrack(stations: readonly Station[], start: Point, phase: number): Track {
  const legs: Leg[] = [leg(0, 0, [start])]
  let floor = 0

  const last = () => legs[legs.length - 1]
  const end = () => last().points[last().points.length - 1]
  // t'den sonraki planları at, t anında yürüyen parçayı orada kes.
  const cut = (t: number) => {
    while (legs.length > 1 && last().t0 > t) legs.pop()
    legs[legs.length - 1] = truncate(last(), t)
  }

  for (const st of stations) {
    if (st.kind === 'dead') {
      // Öldüğü yerde silik kalır; yeniden doğunca üste belirir.
      cut(st.start)
      legs.push(leg(st.start, st.start, [end()], true), leg(st.end, st.end, [st.point]))
      floor = st.end
      continue
    }
    if (st.kind === 'recall') {
      const t = Math.max(st.start, floor)
      cut(t)
      legs.push(leg(t + RECALL_CHANNEL, t + RECALL_CHANNEL, [st.point]))
      floor = t + RECALL_CHANNEL
      continue
    }

    // Olay durağına başlangıcında varmak için yol süresi kadar erken çık; boşta durağına başlangıcında çık.
    const hard = st.kind === 'event'
    let depart = st.start
    if (hard) depart -= pathLength(route(end(), st.point)) / WALK_SPEED
    depart = Math.max(depart, floor)
    cut(depart)
    const from = end()
    const path = route(from, st.point)
    const length = pathLength(path)
    if (length > 0.5) {
      let travel = length / WALK_SPEED
      // Yetişemiyorsa hızlan (en fazla MAX_BOOST kat): olay anında olay yerinde olsun.
      if (hard && st.deadline !== undefined && depart + travel > st.deadline)
        travel = Math.max(length / (WALK_SPEED * MAX_BOOST), st.deadline - depart)
      legs.push(leg(depart, depart + travel, path))
    }
    if (hard) floor = Math.max(floor, st.end, last().t1)
  }
  return { legs, phase }
}

/** Maçın tüm güzergâhları (maç başına bir kez; ~10 şampiyon × ~35 dk). */
export function buildTracks(replay: Replay): Map<string, Track> {
  const states: MatchState[] = []
  for (let t = 0; t <= endTime(replay); t += STEP) states.push(stateAt(replay, t))
  const tracks = new Map<string, Track>()
  for (const [id, c] of roster(replay)) {
    const o = c.side === 'blue' ? 1 : -1
    // Başlangıç: üs, takım arkadaşları yan yana (recall noktasıyla aynı dizilim).
    const start: Point = [BASE[c.side][0] + (c.slot - 2) * 9, BASE[c.side][1] - o * 4]
    const phase = c.slot * 1.7 + (c.side === 'red' ? 0.9 : 0)
    tracks.set(id, buildTrack(stationsOf(replay, states, id, c.side, c.role, c.slot), start, phase))
  }
  return tracks
}

/** Beklerken kıpırdanma genliği (harita birimi): yerinde dikilen ikon cansız duruyor. */
const IDLE_SWAY = 3.5

/** T anında şampiyonun yeri. */
export function poseAt(track: Track, t: number): Pose {
  const { legs } = track
  // t0 ≤ t olan son parça (ikili arama).
  let lo = 0
  let hi = legs.length - 1
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1
    if (legs[mid].t0 <= t) lo = mid
    else hi = mid - 1
  }
  const l = legs[lo]
  const point = pointOn(l, t)
  if (l.dead || t < l.t1) return { point, dead: l.dead }
  // Bekliyor: küçük bir salınım; varınca ve yola çıkmadan önce 3 sn'de söner (yürüyüşe atlamasın).
  const next = legs[lo + 1]
  const ramp = Math.max(0, Math.min(1, (t - l.t1) / 3, next ? (next.t0 - t) / 3 : 1))
  const a = t * 0.35 + track.phase
  return {
    point: [point[0] + Math.sin(a) * IDLE_SWAY * ramp, point[1] + Math.sin(a * 1.53 + 1) * IDLE_SWAY * 0.7 * ramp],
    dead: false,
  }
}
