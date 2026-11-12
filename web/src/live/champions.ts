import { useEffect, useState } from 'react'
import { api } from '../api/http'
import type { Champion } from '../api/types'

// Şampiyon listesi sayfa ömrü boyunca bir kez çekilir (sunucu da 5 dk önbellek başlığı verir).
let cache: Promise<Map<string, Champion>> | null = null

function load(): Promise<Map<string, Champion>> {
  cache ??= api.getChampions().then(
    (list) => new Map(list.map((c) => [c.id, c])),
    (error: unknown) => {
      cache = null // hata kalıcı olmasın: sonraki çağrı yeniden dener
      throw error
    },
  )
  return cache
}

/** Kimlik → şampiyon. Yüklenene kadar null. */
export function useChampions(): Map<string, Champion> | null {
  const [champions, setChampions] = useState<Map<string, Champion> | null>(null)
  useEffect(() => {
    let current = true
    load().then(
      (map) => current && setChampions(map),
      () => undefined,
    )
    return () => {
      current = false
    }
  }, [])
  return champions
}
