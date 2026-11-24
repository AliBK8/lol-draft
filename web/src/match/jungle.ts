// Ormancının programı (DESIGN.md §7): kamptan kampa gerçek bir akış. Motor kamp olayı üretmez (orman akışı puanda,
// §4.5); bu katman sadece "olay yokken ormancı nerede" sorusunu cevaplar. Saf ve maç başına bir kez: aynı olay listesi
// aynı programı verir (geri sarma tutarlı).
//
// Kurallar:
// - Kamplar 1:30'da doğar; temizlenen küçük kamp 2:15, buff 5:00 sonra yeniden doğar.
// - İlk tur sabit rota: üstten (mavi buff → gromp → kurtlar → kuşlar → kırmızı → kurbağa) ya da alttan (tersi).
//   Sonra açgözlü: en erken bitirebileceği kampa gider (yürüme + bekleme + temizleme).
// - Meşgulken (ölü, gank, savaş, recall: engagement.ts) kamp temizlenmez; dönünce kaldığı yerden.
// - Yakında doğacak kamp yoksa: 14. dakikadan önce kendi tarafında nehre (yengeç), sonra mid'e yakın dolaşır.
// - İki ormancı birbirinin kopyası olmasın: başlangıç tarafı ve temizleme hızı (±%15) şampiyondan türetilir.
import type { Side } from '../api/types'
import { deadAt, engagement } from './engagement'
import { BASE, CAMP_KINDS, JUNGLE, WALK_SPEED, type CampKind, type Point } from './geometry'
import type { MatchEvent, River } from './types'

export type JungleTarget =
  | { kind: 'base' }
  | { kind: 'camp'; camp: CampKind }
  | { kind: 'scuttle'; river: River }
  | { kind: 'hover' }

interface Visit {
  start: number
  end: number
  target: JungleTarget
}

const FIRST_SPAWN = 90
const LEAVE_BASE = 45
const SCUTTLE_SPAWN = 210
const HOVER_MINUTE = 14
/** Bu kadar saniyede bitirilebilecek kamp yoksa ormancı dolaşmaya çıkar. */
const IDLE_GAP = 40

// Temizleme süresi (oyun sn) ve yeniden doğma.
const CLEAR: Record<CampKind, number> = { blue: 16, red: 16, gromp: 10, wolves: 10, raptors: 10, krugs: 12 }
const RESPAWN: Record<CampKind, number> = { blue: 300, red: 300, gromp: 135, wolves: 135, raptors: 135, krugs: 135 }

const TOP_START: readonly CampKind[] = ['blue', 'gromp', 'wolves', 'raptors', 'red', 'krugs']
const BOT_START: readonly CampKind[] = ['red', 'krugs', 'raptors', 'wolves', 'blue', 'gromp']

/** Küçük, kararlı bir dize özeti (FNV-1a): şampiyondan başlangıç tarafı ve hız. */
function hash(s: string): number {
  let h = 2166136261
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 16777619)
  return h >>> 0
}

/** Kuş uçuşu × 1,3 (duvarların etrafından dolaşma payı); yer bilinmiyorsa (olaydan dönüş) 12 sn. */
function travel(from: Point | null, to: Point): number {
  if (!from) return 12
  return (Math.hypot(to[0] - from[0], to[1] - from[1]) * 1.3) / WALK_SPEED
}

function buildSchedule(events: readonly MatchEvent[], champion: string, side: Side, slot: number): Visit[] {
  const endT = events.at(-1)?.t ?? 0
  const h = hash(`${champion}:${side}`)
  const route = h % 2 === 0 ? TOP_START : BOT_START
  const pace = 0.85 + ((h >>> 3) % 31) / 100
  const ready = Object.fromEntries(CAMP_KINDS.map((k) => [k, FIRST_SPAWN])) as Record<CampKind, number>
  const rank = (k: CampKind) => route.indexOf(k)
  const busy = (t: number) => deadAt(events, t, champion) || engagement(events, t, champion, side, slot) !== undefined

  const visits: Visit[] = []
  let t = LEAVE_BASE
  let pos: Point | null = BASE[side]
  let firstClear = 0
  let idleCount = 0

  while (t < endT) {
    if (busy(t)) {
      t += 1
      pos = null
      continue
    }

    // Sıradaki kamp: ilk turda rota, sonra en erken bitecek olan (eşitlikte rota sırası).
    let camp: CampKind
    if (firstClear < route.length) camp = route[firstClear]
    else {
      const finish = (k: CampKind) => Math.max(t + travel(pos, JUNGLE[side][k]), ready[k]) + CLEAR[k] * pace
      camp = [...CAMP_KINDS].sort((a, b) => finish(a) - finish(b) || rank(a) - rank(b))[0]
    }
    const point = JUNGLE[side][camp]
    const arrive = t + travel(pos, point)
    const finish = Math.max(arrive, ready[camp]) + CLEAR[camp] * pace

    let target: JungleTarget = { kind: 'camp', camp }
    let end = finish
    // Yengeç doğmadan (3:30) boşta kalan sıradaki kampın başında bekler.
    if (firstClear >= route.length && finish - t > IDLE_GAP && t >= SCUTTLE_SPAWN) {
      // Dolaşma: kamp doğmadan yola çıkacağı ana kadar (en az 5, en çok 25 sn).
      target =
        t / 60 >= HOVER_MINUTE
          ? { kind: 'hover' }
          : { kind: 'scuttle', river: (h + idleCount) % 2 === 0 ? 'top' : 'bottom' }
      idleCount++
      end = Math.min(Math.max(ready[camp] - travel(null, point), t + 5), t + 25)
    }

    // Ziyaret meşguliyetle kesilebilir: kesilen kamp temizlenmemiş sayılır.
    let interrupted = false
    for (let x = Math.ceil(t) + 1; x < end; x++) {
      if (busy(x)) {
        end = x
        interrupted = true
        break
      }
    }
    visits.push({ start: t, end, target })
    if (target.kind === 'camp' && !interrupted) {
      ready[camp] = finish + RESPAWN[camp]
      pos = point
      if (firstClear < route.length) firstClear++
    } else if (target.kind !== 'camp') {
      pos = null
    }
    t = end
  }
  return visits
}

const schedules = new WeakMap<readonly MatchEvent[], Map<string, Visit[]>>()

/** Ormancının T anındaki hedefi (olay yokken). */
export function jungleTarget(events: readonly MatchEvent[], champion: string, side: Side, slot: number, t: number): JungleTarget {
  let bySide = schedules.get(events)
  if (!bySide) schedules.set(events, (bySide = new Map()))
  let visits = bySide.get(champion)
  if (!visits) bySide.set(champion, (visits = buildSchedule(events, champion, side, slot)))

  // t'yi içeren (ya da t'den önceki son) ziyaret: ikili arama.
  let lo = 0
  let hi = visits.length - 1
  // Maç başı: ormancı 0:45'e kadar üste.
  if (hi < 0 || t < visits[0].start) return { kind: 'base' }
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1
    if (visits[mid].start <= t) lo = mid
    else hi = mid - 1
  }
  return visits[lo].target
}
