// Harita geometrisi (DESIGN.md §7): 600×600, mavi üs sol alt, kırmızı sağ üst. Kendi çizimimiz.
import type { Side } from '../api/types'
import type { Lane, ObjectiveKind, River } from './types'

export type Point = readonly [number, number]

/** Koridor yolları mavi üsten kırmızı üsse: s = 0 mavi uç, s = 1 kırmızı uç. */
export const LANE_PATH: Record<Lane, Point[]> = {
  top: [[70, 530], [70, 70], [530, 70]],
  mid: [[70, 530], [530, 70]],
  bot: [[70, 530], [530, 530], [530, 70]],
}

export const BASE: Record<Side, Point> = { blue: [78, 522], red: [522, 78] }

/** Çukurlar. Nehir sol üstten sağ alta (mid'e dik); Baron/Herald üstte, ejder altta. */
export const PIT: Record<ObjectiveKind, Point> = {
  dragon: [400, 400],
  elder: [400, 400],
  herald: [200, 200],
  baron: [200, 200],
  finalFight: [300, 300],
}

export const RIVER_SPOT: Record<River, Point> = { top: [240, 240], bottom: [360, 360] }

/** Orman kampları (ormancı lane fazında bunlar arasında dolaşır). */
export const CAMPS: Record<Side, Point[]> = {
  blue: [[175, 330], [250, 455], [205, 395]],
  red: [[425, 270], [350, 145], [395, 205]],
}

/** Kulelerin koridor üzerindeki yeri (s): dış, iç, inhibitör. */
export const TOWER_S: Record<Side, readonly [number, number, number]> = {
  blue: [0.38, 0.26, 0.13],
  red: [0.62, 0.74, 0.87],
}

const lengths = (path: Point[]) => path.slice(1).map((p, i) => Math.hypot(p[0] - path[i][0], p[1] - path[i][1]))

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
