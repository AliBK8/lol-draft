import { useEffect, useState } from 'react'
import { ApiError, readJson } from '../api/http'
import type { Replay } from './types'

/**
 * Maç belgesi: elde varsa (MatchReady ile geldi) onu, yoksa GET /api/matches/{id} (sayfa yenilendi, paylaşım linki).
 * Belge dondurulmuş (§8.16): bir kez çekilir, değişmez.
 */
export function useReplay(id: string | null, known?: Replay): { replay: Replay | null; error: string | null } {
  const [fetched, setFetched] = useState<{ id: string; replay: Replay } | null>(null)
  const [error, setError] = useState<{ id: string; message: string } | null>(null)
  const needFetch = id !== null && !known && fetched?.id !== id

  useEffect(() => {
    if (!needFetch || id === null) return
    let current = true
    fetch(`/api/matches/${encodeURIComponent(id)}`)
      .then((r) => readJson<Replay>(r))
      .then(
        (replay) => current && setFetched({ id, replay }),
        (e: unknown) => current && setError({ id, message: e instanceof ApiError ? e.message : 'Maç yüklenemedi.' }),
      )
    return () => {
      current = false
    }
  }, [id, needFetch])

  if (id === null) return { replay: null, error: null }
  if (known) return { replay: known, error: null }
  return {
    replay: fetched?.id === id ? fetched.replay : null,
    error: error?.id === id ? error.message : null,
  }
}
