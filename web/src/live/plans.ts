// Oyun planları (DESIGN.md §4.10): kart metinleri. Sayılar motorun nötr değerleri (Tactics.cs); uyumlu draft kazancı
// ×1,25 alır ve bedeli ×0,75 öder, uymayan tersi. Uyumun kendisi sunucudan gelir (motor tek kaynak).
import type { DraftState, PlanFit, PlanId, Side } from '../api/types'

export interface PlanCard {
  id: PlanId
  name: string
  gain: string
  cost: string
}

export const PLANS: readonly PlanCard[] = [
  { id: 'balanced', name: 'Dengeli', gain: 'Ayarlara dokunmaz.', cost: 'Bedeli yok. Süre dolarsa bu oynanır.' },
  {
    id: 'dragonLate',
    name: 'Ejder & late',
    gain: 'Ejder ve Elder savaşlarında güçlü, 25. dakikadan sonra güç +%6.',
    cost: "Herald savaşında zayıf, ilk 20 dakika koridorlar geride.",
  },
  {
    id: 'earlyPressure',
    name: 'Erken baskı',
    gain: "14. dakikaya kadar koridorlar önde, 2 ek gank, erken çatışmada güçlü.",
    cost: "20. dakikadan sonra güç −%6.",
  },
  {
    id: 'botPressure',
    name: 'Bot baskısı',
    gain: "Lane fazında bot önde; gank'ların çoğu bot'a ve daha başarılı.",
    cost: 'Top yalnız kalır: ilk 14 dakika geride.',
  },
  {
    id: 'split',
    name: 'Split',
    gain: 'Split\'e rakip daha az cevap verir, düelloda güçlü; zayıf splitçi de gider.',
    cost: 'Split olunca objektif savaşı 4\'e 5.',
  },
  {
    id: 'fastFinish',
    name: 'Hızlı bitir',
    gain: 'Kuşatmaya daha az altın farkıyla ve daha sık gider.',
    cost: 'Kuşatma daha zor tutulur, throw daha pahalı.',
  },
  {
    id: 'invade',
    name: 'Ormancı istilası',
    gain: '4. dakikada rakip ormana kesin istila, çatışmada güçlü.',
    cost: 'Püskürtülürse ormancı uzun süre geride.',
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
