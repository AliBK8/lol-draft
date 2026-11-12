// Sunucu saati (DESIGN.md §8.12): saniyelik sayaç mesajı yok; durum `deadline` + `serverNow` taşır.
// İstemcinin saati ileri/geri olabilir: fark (offset) her durumda serverNow'dan yeniden ölçülür, geri sayım onunla çizilir.
//
// Örnek: sunucu 12:00:00 diyor, benim saatim 12:00:05 → offset = −5 sn. Bitiş 12:00:30 ise
// kalan = 12:00:30 − (benim saatim + offset) = 30 sn (benim saatimle değil sunucununkiyle).

/** Sunucu saati − yerel saat (ms). Ağ gecikmesi (~onlarca ms) ihmal edilir: geri sayım saniye çözünürlüğünde. */
export function clockOffset(serverNow: string, localNowMs: number): number {
  return Date.parse(serverNow) - localNowMs
}

/** Bitişe kalan ms (0'ın altına inmez). Bitiş yoksa null. */
export function remainingMs(deadline: string | null, offsetMs: number, localNowMs: number): number | null {
  if (!deadline) return null
  return Math.max(0, Date.parse(deadline) - (localNowMs + offsetMs))
}

/** Ekranda gösterilecek saniye (yukarı yuvarlanır: 0,2 sn kala "1", tam bitince "0"). */
export function secondsLeft(ms: number | null): number | null {
  return ms === null ? null : Math.ceil(ms / 1000)
}
