import type { Announcement } from './highlights'

/**
 * Büyük an bandı (§8.29): haritanın üstünde 2 sn; olayın sırasıyla anahtarlanır, her yeni an animasyonu baştan
 * oynatır. Tıklamayı engellemez. Takım başlığın renginden okunur; alt satır log metni (takımı zaten söylüyor).
 */
export function AnnounceBanner({ announcement }: { announcement: Announcement }) {
  const { side, title, detail, icon } = announcement
  return (
    <div className={`announce ${side}`} role="status">
      {icon && <img className="announce-icon" src={`/map/${icon}.png`} alt="" width={44} height={44} />}
      <div className="announce-text">
        <strong>{title}</strong>
        <span>{detail}</span>
      </div>
    </div>
  )
}
