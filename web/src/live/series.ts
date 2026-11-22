import type { Format } from '../api/types'

/** Seriyi almak için gereken galibiyet (Domain: Series; bo3 → 2). */
export const WINS_TO_TAKE: Record<Format, number> = { bo1: 1, bo3: 2, bo5: 3 }
