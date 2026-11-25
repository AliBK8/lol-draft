import { useState } from 'react'
import type { DraftState, PlanId, Side } from '../api/types'
import { SeriesChips, SeriesPips } from '../components/SeriesHeader'
import { FIT_TR, hasChosenPlan, planFits, planName, PLANS } from '../live/plans'
import { teamLabel, type SeriesHeader } from '../live/seriesHeader'
import type { ActionResult, HubMethod } from '../live/useLobbyConnection'
import { StepTimer } from './DraftBoard'

/** Plan fazının süresi (Domain DraftTimings.Default.Plan): süre çubuğu bunun ne kadarının kaldığını gösterir. */
const PLAN_SECONDS = 20

interface Props {
  draft: DraftState
  me: Side
  offsetMs: number
  act: (method: HubMethod, ...args: string[]) => Promise<ActionResult>
  /** Seri bilgisi (adlar, seri skoru, bağlantı): draft'taki gibi bu ekranın tabelasında (§8.43). */
  header?: SeriesHeader
}

// Oyun planı (DESIGN.md §4.10, §8.35, görünüm §8.43): swap'tan sonra 20 sn, iki takım gizlice ve aynı anda seçer.
// Rakip sadece "seçti"yi görür; hangi plan olduğu maç sonunda karnede. Uyum rozeti sunucudan (motor): uyumlu kazanç
// ×1,25 ve bedel ×0,75, uymayan tersi. Kart seç, sonra kilitle (tek tıkla seçilmesin); seçim tek seferlik. Süre
// dolarsa Dengeli. Kartlarda kısa kazanç/bedel; tam açıklama üstüne gelince.
export function PlanBoard({ draft, me, offsetMs, act, header }: Props) {
  const [selected, setSelected] = useState<PlanId>('balanced')
  // Sunucu planı gizli tutar (kendine de söylemez): seçtiğin plan bu ekranın hafızasında. Sayfa yenilenirse sadece
  // "seçtin" görünür.
  const [chosen, setChosen] = useState<PlanId | null>(null)
  const [error, setError] = useState<string | null>(null)
  const fits = planFits(draft, me)
  const locked = hasChosenPlan(draft, me)

  async function lock() {
    const result = await act('ChoosePlan', selected)
    if (result.ok) setChosen(selected)
    setError(result.ok ? null : result.message)
  }

  // Takım plakası (draft tabelasıyla aynı parça): ad + seri noktaları + "Sen", altında seçim durumu. Seçmeyi
  // bekleyenin adı parlak (göz ona), seçmişinki soluk.
  const plate = (side: Side) => {
    const done = hasChosenPlan(draft, side)
    return (
      <div className={`hud-team draft-plate ${side}${done ? '' : ' active'}`}>
        <span className="hud-id draft-team">
          <span className="hud-name">
            <span className="hud-label draft-team-label">{teamLabel(side, header)}</span>
            <SeriesPips side={side} header={header} />
            {me === side && <span className="you-tag">Sen</span>}
          </span>
          {header?.teams[side].presence}
        </span>
        <span className={`plan-status${done ? ' done' : ''}`}>{done ? 'Seçti ✓' : 'Seçiyor…'}</span>
      </div>
    )
  }

  return (
    <div className="plan">
      <div className="hud draft-hud">
        {plate('blue')}
        <div className="hud-center draft-center">
          <span className="draft-phase">
            <SeriesChips header={header} />
            <span>Oyun planı · gizli</span>
          </span>
          <StepTimer deadline={draft.deadline} offsetMs={offsetMs} side={null} totalSeconds={PLAN_SECONDS} />
        </div>
        {plate('red')}
      </div>

      <div className="plan-grid">
        {PLANS.map((plan) => {
          const fit = fits[plan.id]
          return (
            <button
              key={plan.id}
              className={['plan-card', selected === plan.id ? 'selected' : '', chosen === plan.id ? 'chosen' : ''].join(' ')}
              disabled={locked}
              title={plan.detail}
              onClick={() => setSelected(plan.id)}
            >
              <span className="plan-head">
                <strong>{plan.name}</strong>
                {fit && plan.id !== 'balanced' && <span className={`fit ${fit}`}>{FIT_TR[fit]}</span>}
              </span>
              <span className="plan-gain">▲ {plan.gain}</span>
              {plan.cost && <span className="plan-cost">▼ {plan.cost}</span>}
            </button>
          )
        })}
      </div>

      <div className="plan-foot">
        <button className="primary" disabled={locked} onClick={lock}>
          {locked ? (chosen ? `Planın: ${planName(chosen)}` : 'Planını seçtin') : `Planı kilitle: ${planName(selected)}`}
        </button>
      </div>
      {error && <p className="error">{error}</p>}
    </div>
  )
}
