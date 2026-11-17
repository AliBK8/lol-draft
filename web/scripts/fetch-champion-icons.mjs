// Şampiyon portrelerini Riot Data Dragon'dan bir kez indirir: web/public/champions/{id}.png (DESIGN.md §8.17).
// Kendi sunucumuzdan sunulur: oyuncunun tarayıcısı Riot'a istek atmaz, CSP değişmez, sürüm sabit kalır.
// Çalıştırma: npm run icons  (şampiyon listesi değişince; sonuç repoya girer)
//
// Eşleştirme: bizim kimliğimiz (renekton) ile Data Dragon anahtarı (Renekton, MonkeyKing...) her zaman aynı değil;
// ada göre eşleşir (harf dışı karakterler atılır: "Kai'Sa" = "kaisa"). Eşleşmeyen olursa betik durur, sessizce atlamaz.
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..')
const out = join(root, 'web', 'public', 'champions')
const DDRAGON = 'https://ddragon.leagueoflegends.com'

const normalize = (name) => name.toLowerCase().normalize('NFD').replace(/[^a-z]/g, '')

async function json(url) {
  const response = await fetch(url)
  if (!response.ok) throw new Error(`${url}: ${response.status}`)
  return response.json()
}

const { champions } = JSON.parse(await readFile(join(root, 'data', 'champions.json'), 'utf8'))
const [version] = await json(`${DDRAGON}/api/versions.json`)
const { data } = await json(`${DDRAGON}/cdn/${version}/data/en_US/champion.json`)

// Data Dragon: ad → anahtar (resim dosyasının adı anahtardan gelir).
const byName = new Map(Object.values(data).flatMap((c) => [[normalize(c.name), c.id], [normalize(c.id), c.id]]))

const missing = champions.filter((c) => !byName.has(normalize(c.name)) && !byName.has(normalize(c.id)))
if (missing.length > 0) {
  throw new Error(`Data Dragon'da bulunamadı: ${missing.map((c) => c.id).join(', ')}`)
}

await mkdir(out, { recursive: true })
for (const champion of champions) {
  const key = byName.get(normalize(champion.name)) ?? byName.get(normalize(champion.id))
  const response = await fetch(`${DDRAGON}/cdn/${version}/img/champion/${key}.png`)
  if (!response.ok) throw new Error(`${champion.id} (${key}): ${response.status}`)
  await writeFile(join(out, `${champion.id}.png`), Buffer.from(await response.arrayBuffer()))
}

// Hangi yamadan alındığı: Riot'un fan projesi politikası kaynağı belirtmeyi ister, güncellemede de işe yarar.
await writeFile(join(out, 'SOURCE.txt'), `Riot Games Data Dragon ${version}\n${DDRAGON}\n`)
console.log(`${champions.length} portre indirildi (Data Dragon ${version}).`)

// Maç ekranının haritası: Summoner's Rift (map11), aynı yamadan → web/public/map/map11.png (DESIGN.md §7).
const mapOut = join(root, 'web', 'public', 'map')
await mkdir(mapOut, { recursive: true })
const map = await fetch(`${DDRAGON}/cdn/${version}/img/map/map11.png`)
if (!map.ok) throw new Error(`map11: ${map.status}`)
await writeFile(join(mapOut, 'map11.png'), Buffer.from(await map.arrayBuffer()))

// Kule ve nexus ikonları: oyunun minimap ikonları (gri; takım rengini SVG filtresi verir). Data Dragon'da yok,
// CommunityDragon oyun dosyalarından çıkarıp yama klasörüyle yayınlar: aynı yamaya sabit (16.22.1 → 16.22).
const CDRAGON = 'https://raw.communitydragon.org'
const patch = version.split('.').slice(0, 2).join('.')
for (const icon of ['tower.png', 'inhibitor.png', 'nexus.png']) {
  const response = await fetch(`${CDRAGON}/${patch}/game/assets/ux/minimap/icons/${icon}`)
  if (!response.ok) throw new Error(`${icon}: ${response.status}`)
  await writeFile(join(mapOut, icon), Buffer.from(await response.arrayBuffer()))
}

await writeFile(
  join(mapOut, 'SOURCE.txt'),
  `map11.png: Riot Games Data Dragon ${version}\n${DDRAGON}\n` +
    `tower.png, inhibitor.png, nexus.png: CommunityDragon ${patch} (game/assets/ux/minimap/icons)\n${CDRAGON}\n`,
)
console.log(`Harita, kule, inhibitör ve nexus ikonu indirildi (Data Dragon ${version}, CommunityDragon ${patch}).`)
