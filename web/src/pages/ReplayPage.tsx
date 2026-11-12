import { useParams } from 'react-router-dom'
import { useChampions } from '../live/champions'
import { MatchPlayer } from '../match/MatchPlayer'
import { useReplay } from '../match/useReplay'

// Tekrar izleme (/m/:id): paylaşılan link. Token istemez (kimlik 128 bit, §8.15); belge diskten (§8.16).
// Lobi bilinmediği için "sen" yok: sonuç "Mavi kazandı" diye anlatılır.
export function ReplayPage() {
  const { id = '' } = useParams()
  const { replay, error } = useReplay(id)
  const champions = useChampions()

  if (error) return <p className="card">{error}</p>
  if (!replay || !champions) return <p className="muted">Maç yükleniyor…</p>
  return (
    <section className="card">
      <h1>Maç {replay.gameNumber}</h1>
      <MatchPlayer replay={replay} champions={champions} />
    </section>
  )
}
