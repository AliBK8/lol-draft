import { useState } from 'react'
import type { DraftState, PlanId, Side } from '../api/types'
import { Countdown } from '../components/Countdown'
import { otherSide } from '../live/draft'
import { FIT_TR, hasChosenPlan, planFits, planName, PLANS } from '../live/plans'
import type { ActionResult, HubMethod } from '../live/useLobbyConnection'

interface Props {
  draft: DraftState
  me: Side
  offsetMs: number
  act: (method: HubMethod, ...args: string[]) => Promise<ActionResult>
}

// Oyun planı (DESIGN.md §4.10, §8.35): swap'tan sonra 20 sn, iki takım gizlice ve aynı anda seçer. Rakip sadece
// "seçti"yi görür; hangi plan olduğu maç sonunda karnede. Uyum rozeti sunucudan (motor): uyumlu kazanç ×1,25 ve
// bedel ×0,75, uymayan tersi. Kart seç, sonra kilitle (tek tıkla seçilmesin); seçim tek seferlik. Süre dolarsa Dengeli.
export function PlanBoard({ draft, me, offsetMs, act }: Props) {
  const [selected, setSelected] = useState<PlanId>('balanced')
  // Sunucu planı gizli tutar (kendine de söylemez): seçtiğin plan bu ekranın hafızasında. Sayfa yenilenirse sadece
  // "seçtin" görünür.
  const [chosen, setChosen] = useState<PlanId | null>(null)
  const [error, setError] = useState<string | null>(null)
  const fits = planFits(draft, me)
  const locked = hasChosenPlan(draft, me)
  const rivalLocked = hasChosenPlan(draft, otherSide(me))

  async function lock() {
    const result = await act('ChoosePlan', selected)
    if (result.ok) setChosen(selected)
    setError(result.ok ? null : result.message)
  }

  return (
    <div className="plan">
      <div className="draft-bar">
        <span>Oyun planı</span>
        <Countdown deadline={draft.deadline} offsetMs={offsetMs} />
        <span className="muted small">Rakip planını maç sonunda görür. Uyumlu plan daha iyi işler.</span>
      </div>

      <div className="plan-grid">
        {PLANS.map((plan) => {
          const fit = fits[plan.id]
          return (
            <button
              key={plan.id}
              className={['plan-card', selected === plan.id ? 'selected' : '', chosen === plan.id ? 'chosen' : ''].join(' ')}
              disabled={locked}
              onClick={() => setSelected(plan.id)}
            >
              <span className="plan-head">
                <strong>{plan.name}</strong>
                {fit && plan.id !== 'balanced' && <span className={`fit ${fit}`}>{FIT_TR[fit]}</span>}
              </span>
              <span className="plan-gain">+ {plan.gain}</span>
              <span className="plan-cost">− {plan.cost}</span>
            </button>
          )
        })}
      </div>

      <div className="plan-foot">
        <button className="primary" disabled={locked} onClick={lock}>
          {locked ? (chosen ? `Planın: ${planName(chosen)}` : 'Planını seçtin') : `Planı kilitle: ${planName(selected)}`}
        </button>
        <span className="muted">Rakip: {rivalLocked ? 'seçti ✓' : 'seçiyor…'}</span>
      </div>
      {error && <p className="error">{error}</p>}
    </div>
  )
}
