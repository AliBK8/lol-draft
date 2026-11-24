import type { Side } from '../api/types'
import type { SeriesHeader } from '../live/seriesHeader'

/**
 * Seri skoru noktaları (espor yayınlarındaki gibi): seriyi almak için gereken kadar nokta, kazanılan kadarı dolu.
 * Örnek: bo3'te 1 galibiyet → ●○. Bo1'de gösterilmez (tek maç, seri yok).
 */
export function SeriesPips({ side, header }: { side: Side; header?: SeriesHeader }) {
  if (!header || header.need < 2) return null
  const wins = header.teams[side].wins
  return (
    <span className={`pips ${side}`} aria-label={`Seride ${wins} galibiyet`}>
      {Array.from({ length: header.need }, (_, i) => (
        <i key={i} className={i < wins ? 'on' : ''} />
      ))}
    </span>
  )
}

/** BO3 ve Fearless rozetleri (şeftali), bandın ortasında küçük. */
export function SeriesChips({ header }: { header?: SeriesHeader }) {
  if (!header) return null
  return (
    <span className="series-chips">
      <span className="chip accent">{header.format.toUpperCase()}</span>
      {header.fearless && <span className="chip accent">Fearless</span>}
    </span>
  )
}
