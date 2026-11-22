import { useEffect, useState } from 'react'
import slides from './backdrop.json'

/** Ana sayfanın arka planı: kostüm splash'ları (tam boy, npm run icons -- --hero; liste betikle ortak). */
interface Slide {
  id: string
  skin: number
  name: string
}

const SLIDES: Slide[] = slides
const INTERVAL_MS = 5000

// Dönen arka plan (§8.31): görseller üst üste durur, sadece görünenin opaklığı açık; 5 sn'de bir sıradakine geçilir,
// geçişi CSS yapar (opaklık + hafif yakınlaşma). Her ziyaret rastgele bir görselden başlar.
// Yükleme tembel: sadece şimdiye kadar görünenler ve sıradaki çizilir (sayfa açılışında 6 görsel birden inmesin).
// Hareket azaltma tercihi olanda döngü yok, tek görsel kalır.
export function HomeBackdrop() {
  const [start] = useState(() => Math.floor(Math.random() * SLIDES.length))
  const [step, setStep] = useState(0) // kaç kez geçildi
  const [still] = useState(() => globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false)

  useEffect(() => {
    if (still) return
    const timer = setInterval(() => setStep((s) => s + 1), INTERVAL_MS)
    return () => clearInterval(timer)
  }, [still])

  const active = (start + step) % SLIDES.length
  // Kaç görsel çizilir: görünenler (step + 1) ve bir sonraki (ön yükleme); döngü tamamlanınca hepsi.
  const loaded = still ? 1 : Math.min(step + 2, SLIDES.length)

  return (
    <div className="home-backdrop" aria-hidden="true">
      {SLIDES.map((slide, i) => {
        const order = (i - start + SLIDES.length) % SLIDES.length // bu görsel başlangıçtan kaç adım sonra gelir
        if (order >= loaded) return null
        return (
          <img
            key={`${slide.id}-${slide.skin}`}
            src={`/champions/hero/${slide.id}-${slide.skin}.jpg`}
            alt=""
            className={i === active ? 'on' : undefined}
          />
        )
      })}
    </div>
  )
}
