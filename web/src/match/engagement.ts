// Şampiyon şu an bir olayla meşgul mü, meşgulse nerede (DESIGN.md §7, §8.26). Sadece olay listesine bakar: konum
// katmanı (positions.ts) bunu noktaya çevirir, ormancının programı (jungle.ts) "meşgul mü"yu sorar. Kurallar tek
// yerde: iki katman aynı pencereleri görür.
import type { Side } from '../api/types'
import { BASE, JUNGLE, pathPoint, PIT, RIVER_SPOT, TOWER_S, type Point } from './geometry'
import type { Lane, MatchEvent, ObjectiveKind } from './types'

export type Engagement =
  /** Savaş dizilişi (objektif, kuşatma, nehir çatışması, istila): merkez ve olayın saniyesi. */
  | { kind: 'fight'; t: number; center: Point }
  /** Koridora hareket (gank): yeri koridorun ön hattına göre (konum katmanı hesaplar). */
  | { kind: 'lane'; t: number; lane: Lane }
  /** Split ya da ona cevap. */
  | { kind: 'split'; t: number; lane: Lane; attacker: Side; splitter: boolean }
  /** Recall: üsse kısa dönüş. */
  | { kind: 'recall' }

const other = (side: Side): Side => (side === 'blue' ? 'red' : 'blue')

// Recall: oyuncu başına kaydırılmış periyotla (270 sn) 14 sn'lik pencere; ilk recall 6:00-10:30 arası (eskiden
// 1:30'dan başlıyordu: minyon gelmeden üsse dönen koridorcu, ilk turunun ortasında dönen ormancı). Yaklaşan savaştan
// (objektif, kuşatma) önce basılmaz: pencerenin başından 14 + 45 sn içinde savaş varsa o recall atlanır (yoksa üsten
// çukura koşarak yetişiyordu).
const RECALL_PERIOD = 270
const RECALL_WINDOW = 14
const RECALL_CLEARANCE = 45

/** [from, from + span] içinde objektif ya da kuşatma var mı (olaylar zaman sırasında). */
function fightSoon(events: readonly MatchEvent[], from: number, span: number): boolean {
  for (const e of events) {
    if (e.t > from + span) return false
    if (e.t >= from && (e.type === 'objective' || e.type === 'siege')) return true
  }
  return false
}

/**
 * Kuşatmanın merkezi: koridoru biliniyorsa (§4.11) o koridorda savunanın inhibitör kulesinin hemen önü (saldıranın
 * tarafına doğru); bilinmiyorsa (eski kayıt, Nexus'a yürüyüş) savunanın üssünün önü.
 */
function siegeCenter(attacker: Side, lane?: Lane | null): Point {
  const defender = other(attacker)
  if (lane) return pathPoint(lane, TOWER_S[defender][lane][2] + (defender === 'blue' ? 0.04 : -0.04))
  const [bx, by] = BASE[defender]
  const d = attacker === 'red' ? 1 : -1
  return [bx + d * 95, by - d * 95]
}

// Arkadan gelen savaştan en fazla bu kadar sn önce yola çıkar (yetişemezse hızlanır: geç ve koşarak gelir).
const FLANK_LATE = 9

/** Arkadan gelenin savaş noktası: çukurdan rakibin üssüne doğru 45 birim (rakibin arkası). */
function flankCenter(objective: ObjectiveKind, side: Side): Point {
  const [px, py] = PIT[objective]
  const [bx, by] = BASE[other(side)]
  const len = Math.hypot(bx - px, by - py) || 1
  return [px + ((bx - px) / len) * 45, py + ((by - py) / len) * 45]
}

/** T anında şampiyonu meşgul eden olay (ölüm hariç: ölüyü konum katmanı ayrıca bilir). */
export function engagement(
  events: readonly MatchEvent[],
  t: number,
  champion: string,
  side: Side,
  slot: number,
): Engagement | undefined {
  for (const e of events) {
    if (e.t > t + 25) break
    switch (e.type) {
      case 'flank':
        // Arkadan kapatma (§4.11 D): yan koridordan geç çıkar (olaydan 22-9 sn önce hâlâ koridorunda), savaşa rakibin
        // arkasından girer. Olay objektifle aynı saniyede ve ondan önce listede: objektif penceresini bu karşılar.
        if (e.flankers.includes(champion) && t >= e.t - 22 && t <= e.t + 5) {
          if (t < e.t - FLANK_LATE) return undefined
          return { kind: 'fight', t: e.t, center: flankCenter(e.objective, e.side) }
        }
        break
      case 'objective':
        if (t >= e.t - 22 && t <= e.t + 5) return { kind: 'fight', t: e.t, center: PIT[e.objective] }
        break
      case 'siege':
        if (t >= e.t - e.lead && t <= e.t + e.tail) return { kind: 'fight', t: e.t, center: siegeCenter(e.side, e.lane) }
        break
      case 'move':
        if (e.participants.includes(champion) && t >= e.t - e.lead && t <= e.t + e.tail) {
          if (e.river) return { kind: 'fight', t: e.t, center: RIVER_SPOT[e.river] }
          if (e.lane) return { kind: 'lane', t: e.t, lane: e.lane }
        }
        break
      case 'invade':
        // İstila: rakibin kampında (savunanın ormanı), iki tarafın katılanları savaş dizilişinde.
        if ((e.invaders.includes(champion) || e.defenders.includes(champion)) && t >= e.t - e.lead && t <= e.t + e.tail)
          return { kind: 'fight', t: e.t, center: JUNGLE[other(e.side)][e.camp] }
        break
      case 'catch':
        // Yakalanma (§4.11 D): avcılar, kurban ve yardıma gelenler yerde: ormandaysa o kampta, koridordaysa ön hatta.
        if ((e.hunters.includes(champion) || e.helpers.includes(champion) || e.victim === champion)
          && t >= e.t - e.lead && t <= e.t + e.tail) {
          if (e.jungle && e.camp) return { kind: 'fight', t: e.t, center: JUNGLE[e.jungle][e.camp] }
          if (e.lane) return { kind: 'lane', t: e.t, lane: e.lane }
        }
        break
      case 'split':
        if ((e.splitter === champion || e.answerer === champion) && t >= e.t - 20 && t <= e.t + 4)
          return { kind: 'split', t: e.t, lane: e.lane, attacker: e.side, splitter: e.splitter === champion }
        break
    }
  }

  const offset = 60 * slot + (side === 'red' ? 25 : 0) + 90 + RECALL_PERIOD
  const windowStart = t - ((t - offset) % RECALL_PERIOD)
  if (t > offset && t - windowStart < RECALL_WINDOW && !fightSoon(events, windowStart, RECALL_WINDOW + RECALL_CLEARANCE))
    return { kind: 'recall' }
  return undefined
}

/** T anında ölü mü (kill olaylarından; timeline.stateAt ile aynı kural: until'e kadar ölü). */
export function deadAt(events: readonly MatchEvent[], t: number, champion: string): boolean {
  let dead = false
  for (const e of events) {
    if (e.t > t) break
    if (e.type === 'kill' && e.victim === champion) dead = e.until > t
  }
  return dead
}
