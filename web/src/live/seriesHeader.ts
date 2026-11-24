import type { ReactNode } from 'react'
import type { Format, Side } from '../api/types'

/** Bir takımın bantta görünen seri bilgisi. */
export interface TeamHeader {
  /** Takım adı (lobide verildiyse); yoksa bant renk adını yazar. */
  name: string | null
  /** Seride görünür galibiyet (izlenmemiş maç sayılmaz, §8.15). */
  wins: number
  /** Bağlantı durumu (nokta, koptuysa hükmen sayacı); lobi verir. */
  presence?: ReactNode
}

/**
 * Draft başlığı ve maç skor bandının taşıdığı seri bilgisi (§8.37): ayrı seri bandı yerine tek bant.
 * Paylaşım sayfasında (`/m/{id}`) yok; bant sadece renk adlarını yazar.
 */
export interface SeriesHeader {
  format: Format
  fearless: boolean
  /** Seriyi almak için gereken galibiyet (bo3 → 2): nokta sayısı. */
  need: number
  teams: Record<Side, TeamHeader>
}

export const SIDE_TR: Record<Side, string> = { blue: 'Mavi', red: 'Kırmızı' }

/** Bantta takımın adı: verdiği ad, yoksa renk adı. */
export function teamLabel(side: Side, header?: SeriesHeader): string {
  return header?.teams[side].name ?? SIDE_TR[side]
}
