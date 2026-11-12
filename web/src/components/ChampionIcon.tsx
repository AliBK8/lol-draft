import type { Side } from '../api/types'

interface Props {
  id: string | null
  name?: string
  side?: Side
  size?: number
  /** Ban'lı / kullanılamaz: soluk ve gri. */
  dim?: boolean
}

// Portre: /champions/{id}.png (Data Dragon'dan indirilip kendi sunucumuzdan, §8.17). Boş yuva için çerçeve.
export function ChampionIcon({ id, name, side, size = 48, dim = false }: Props) {
  const className = ['champ', side ?? '', dim ? 'dim' : ''].join(' ').trim()
  if (!id) return <span className={`${className} empty`} style={{ width: size, height: size }} />
  return (
    <img
      className={className}
      src={`/champions/${id}.png`}
      alt={name ?? id}
      title={name ?? id}
      width={size}
      height={size}
      loading="lazy"
    />
  )
}
