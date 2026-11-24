// Maçın büyük anları (UYGULAMA.md §8.29): duyuru bandı ve zaman çizelgesi işaretleri. Saf: T'den (ve oynatma
// hızından) türetilir, zamanlayıcı yok; geri sarınca, atlayınca da doğru.
import type { Side } from '../api/types'
import { winnerSide, type MatchEvent } from './types'

export interface Announcement {
  /** Olayın belgedeki sırası: bant bu anahtarla takılır (yeni an yeni animasyon). */
  index: number
  side: Side
  title: string
  /** Alt satır: olayın log metni. */
  detail: string
  /** Oyunun minimap ikonu (`/map/{icon}.png`), yoksa null. */
  icon: string | null
}

/** Bant bu kadar gerçek saniye kalır (CSS animasyonuyla aynı). */
export const BANNER_SECONDS = 2

/** Olay duyurulacak büyüklükte mi; öyleyse başlık ve ikon. İlk kan: log metni "İlk kan!" ile başlayan kill. */
function headline(e: MatchEvent): Omit<Announcement, 'index' | 'side' | 'detail'> | null {
  switch (e.type) {
    case 'kill':
      return e.text.startsWith('İlk kan') ? { title: 'İLK KAN', icon: null } : null
    case 'objective':
      return {
        dragon: { title: 'EJDER', icon: 'dragon' },
        elder: { title: 'ELDER EJDERİ', icon: 'dragon_elder' },
        herald: { title: 'HERALD', icon: 'riftherald' },
        baron: { title: 'BARON NASHOR', icon: 'baron' },
        finalFight: { title: 'SON TAKIM SAVAŞI', icon: null },
      }[e.objective]
    case 'dragonSoul':
      return { title: 'EJDER RUHU', icon: 'dragon' }
    case 'invade':
      return { title: e.stolen ? 'ORMAN İSTİLASI' : 'İSTİLA PÜSKÜRTÜLDÜ', icon: null }
    case 'throw':
      return { title: 'THROW!', icon: null }
    case 'nexus':
      return { title: 'NEXUS DÜŞTÜ', icon: 'nexus' }
    default:
      return null
  }
}

/**
 * T anında gösterilecek duyuru: son `window` oyun saniyesindeki en yeni büyük an. Pencere = bant süresi × oynatma
 * hızı (oyun sn / gerçek sn): bant her hızda ~2 gerçek saniye kalır.
 */
export function announcementAt(events: readonly MatchEvent[], t: number, window: number): Announcement | null {
  for (let i = events.length - 1; i >= 0; i--) {
    const e = events[i]
    if (e.t > t) continue
    if (t - e.t > window) break
    const head = headline(e)
    const side = winnerSide(e)
    if (head && side) return { index: i, side, detail: e.text, ...head }
  }
  return null
}

export interface Marker {
  /** 0-1: maçın neresinde. */
  at: number
  side: Side | null
  /** Kill çizgi; diğerleri ikon. */
  kind: 'kill' | 'icon'
  icon: string | null
  label: string
}

const OBJECTIVE_ICON = { dragon: 'dragon', elder: 'dragon_elder', herald: 'riftherald', baron: 'baron' } as const

/** Zaman çizelgesi işaretleri: kill'ler (takım renginde çizgi), canavarlar, kuleler, nexus (ikon). */
export function timelineMarkers(events: readonly MatchEvent[], end: number): Marker[] {
  const markers: Marker[] = []
  for (const e of events) {
    const at = Math.min(1, e.t / end)
    if (e.type === 'kill') markers.push({ at, side: e.side, kind: 'kill', icon: null, label: e.text })
    else if (e.type === 'objective' && e.objective !== 'finalFight')
      markers.push({ at, side: e.side, kind: 'icon', icon: OBJECTIVE_ICON[e.objective], label: e.text })
    else if (e.type === 'tower') markers.push({ at, side: e.side, kind: 'icon', icon: 'tower', label: e.text })
    else if (e.type === 'nexus') markers.push({ at, side: e.side, kind: 'icon', icon: 'nexus', label: e.text })
  }
  return markers
}
