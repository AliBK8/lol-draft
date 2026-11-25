// Oyun planları (DESIGN.md §4.10): kart metinleri. Sayılar motorun nötr değerleri (Tactics.cs); uyumlu draft kazancı
// ×1,25 alır ve bedeli ×0,75 öder, uymayan tersi. Uyumun kendisi sunucudan gelir (motor tek kaynak).
import type { DraftState, PlanFit, PlanId, Side } from '../api/types'

export interface PlanCard {
  id: PlanId
  name: string
  /** Kartta görünen kısa kazanç ve bedel (2-5 kelime, §8.43). Bedeli olmayan planda boş. */
  gain: string
  cost: string
  /** Tam açıklama: kartın üstüne gelince (title). */
  detail: string
}

export const PLANS: readonly PlanCard[] = [
  { id: 'balanced', name: 'Dengeli', gain: 'Ayarlara dokunmaz', cost: '', detail: 'Ayarlara dokunmaz, bedeli yok. Süre dolarsa bu oynanır.' },
  {
    id: 'dragonLate',
    name: 'Ejder & late',
    gain: 'Ejder, Elder, 25 dk sonrası',
    cost: 'Herald, ilk 20 dk',
    detail: 'Ejder ve Elder savaşlarında güçlü, 25. dakikadan sonra güç +%6. Herald savaşında zayıf, ilk 20 dakika koridorlar geride.',
  },
  {
    id: 'earlyPressure',
    name: 'Erken baskı',
    gain: 'İlk 14 dk, 2 ek gank',
    cost: '20 dk sonrası zayıf',
    detail: '14. dakikaya kadar koridorlar önde, 2 ek gank, erken çatışmada güçlü. 20. dakikadan sonra güç −%6.',
  },
  {
    id: 'botPressure',
    name: 'Bot baskısı',
    gain: "Bot koridoru, gank'lar",
    cost: 'Top yalnız, ilk 14 dk',
    detail: "Lane fazında bot önde; gank'ların çoğu bot'a ve daha başarılı. Top yalnız kalır: ilk 14 dakika geride.",
  },
  {
    id: 'split',
    name: 'Split',
    gain: 'Split ve düello',
    cost: "Objektifte 4'e 5",
    detail: "Split'e rakip daha az cevap verir, düelloda güçlü; zayıf splitçi de gider. Split olunca objektif savaşı 4'e 5.",
  },
  {
    id: 'fastFinish',
    name: 'Hızlı bitir',
    gain: 'Erken ve sık kuşatma',
    cost: 'Throw pahalı',
    detail: 'Kuşatmaya daha az altın farkıyla ve daha sık gider. Kuşatma daha zor tutulur, throw daha pahalı.',
  },
  {
    id: 'invade',
    name: 'Ormancı istilası',
    gain: '4. dk kesin istila',
    cost: 'Püskürtülürse geride',
    detail: '4. dakikada rakip ormana kesin istila, çatışmada güçlü. Püskürtülürse ormancı uzun süre geride.',
  },
]

export const FIT_TR: Record<PlanFit, string> = { good: 'uyumlu', neutral: 'nötr', poor: 'uymuyor' }

export function planName(id: PlanId): string {
  return PLANS.find((p) => p.id === id)?.name ?? id
}

/** Takımın planlara uyumu (plan fazında; yoksa boş). */
export function planFits(draft: DraftState, side: Side): Partial<Record<PlanId, PlanFit>> {
  return (side === 'blue' ? draft.bluePlanFits : draft.redPlanFits) ?? {}
}

export function hasChosenPlan(draft: DraftState, side: Side): boolean {
  return side === 'blue' ? draft.bluePlanChosen : draft.redPlanChosen
}
