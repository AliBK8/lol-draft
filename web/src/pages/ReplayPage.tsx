import { useParams } from 'react-router-dom'

// Tekrar izleme (/m/:id): maç oynatıcısıyla birlikte 5c'de.
export function ReplayPage() {
  const { id } = useParams()
  return <p className="muted">Maç {id}: tekrar izleme yakında.</p>
}
