import { useEffect, useState } from 'react'
import { remainingMs, secondsLeft } from '../live/clock'

// Geri sayım: sunucunun bitiş anı + saat farkı (§8.12). Saniyede 4 kez çizilir; sunucudan saniyelik mesaj gelmez.
export function Countdown({ deadline, offsetMs }: { deadline: string | null; offsetMs: number }) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!deadline) return
    const timer = setInterval(() => setNow(Date.now()), 250)
    return () => clearInterval(timer)
  }, [deadline])

  const seconds = secondsLeft(remainingMs(deadline, offsetMs, now))
  if (seconds === null) return null
  return <span className={seconds <= 5 ? 'countdown urgent' : 'countdown'}>{seconds}</span>
}
