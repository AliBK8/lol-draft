// Şampiyon portrelerini Riot Data Dragon'dan bir kez indirir: web/public/champions/{id}.png (DESIGN.md §8.17).
// Kendi sunucumuzdan sunulur: oyuncunun tarayıcısı Riot'a istek atmaz, CSP değişmez, sürüm sabit kalır.
// Çalıştırma: npm run icons  (şampiyon listesi değişince; sonuç repoya girer). Yama sabitlemek için:
// npm run icons -- 16.22.1 (verilmezse en son yama; mevcut dosyaları yeni yamaya taşımadan ekleme yapmak için).
// Sadece ana sayfanın arka plan görselleri (tam boy, §8.31): npm run icons -- --hero
//
// Eşleştirme: bizim kimliğimiz (renekton) ile Data Dragon anahtarı (Renekton, MonkeyKing...) her zaman aynı değil;
// ada göre eşleşir (harf dışı karakterler atılır: "Kai'Sa" = "kaisa"). Eşleşmeyen olursa betik durur, sessizce atlamaz.
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import sharp from 'sharp'

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..')
const out = join(root, 'web', 'public', 'champions')
const DDRAGON = 'https://ddragon.leagueoflegends.com'

const normalize = (name) => name.toLowerCase().normalize('NFD').replace(/[^a-z]/g, '')

async function json(url) {
  const response = await fetch(url)
  if (!response.ok) throw new Error(`${url}: ${response.status}`)
  return response.json()
}

/** Ağ kopmasında (ECONNRESET vb.) 3 kez daha dener; HTTP hatası (403, 404) denenmez, çağırana döner. */
async function fetchRetry(url) {
  for (let attempt = 1; ; attempt++) {
    try {
      return await fetch(url)
    } catch (error) {
      if (attempt >= 4) throw error
      await new Promise((resolve) => setTimeout(resolve, 1000 * attempt))
    }
  }
}

/** Splash dosya adı anahtardan farklı olanlar (Data Dragon tutarsızlığı: centered/Fiddlesticks_0.jpg 403). */
const SPLASH_KEY = { Fiddlesticks: 'FiddleSticks' }

const args = process.argv.slice(2)
const heroOnly = args.includes('--hero')
const { champions } = JSON.parse(await readFile(join(root, 'data', 'champions.json'), 'utf8'))
const version = args.find((a) => !a.startsWith('--')) ?? (await json(`${DDRAGON}/api/versions.json`))[0]
const { data } = await json(`${DDRAGON}/cdn/${version}/data/en_US/champion.json`)

// Data Dragon: ad → anahtar (resim dosyasının adı anahtardan gelir).
const byName = new Map(Object.values(data).flatMap((c) => [[normalize(c.name), c.id], [normalize(c.id), c.id]]))

const missing = champions.filter((c) => !byName.has(normalize(c.name)) && !byName.has(normalize(c.id)))
if (missing.length > 0) {
  throw new Error(`Data Dragon'da bulunamadı: ${missing.map((c) => c.id).join(', ')}`)
}

const keyOf = (champion) => byName.get(normalize(champion.name)) ?? byName.get(normalize(champion.id))
// Kostüm numarası: 0 temel kostüm; diğerleri Data Dragon'daki şampiyon belgesinin `skins[].num` alanı.
const splashUrl = (key, skin = 0) => `${DDRAGON}/cdn/img/champion/centered/${SPLASH_KEY[key] ?? key}_${skin}.jpg`

// Ana sayfanın dönen arka planı (§8.31): kostüm splash'ları tam boy (1280×720). Kart için küçültülmüş 640'lık
// sürüm geniş ekranda ~1100 px'e büyüyüp bulanıklaşıyordu. Liste sayfayla ortak: web/src/pages/backdrop.json
// ({ id, skin, name }) → champions/hero/{id}-{skin}.jpg.
const heroes = JSON.parse(await readFile(join(root, 'web', 'src', 'pages', 'backdrop.json'), 'utf8'))
const heroOut = join(out, 'hero')
await mkdir(heroOut, { recursive: true })
for (const { id, skin } of heroes) {
  const champion = champions.find((c) => c.id === id)
  if (!champion) throw new Error(`backdrop.json: havuzda yok: ${id}`)
  const splash = await fetchRetry(splashUrl(keyOf(champion), skin))
  if (!splash.ok) throw new Error(`${id} kostüm ${skin}: ${splash.status}`)
  const full = await sharp(Buffer.from(await splash.arrayBuffer()))
    .resize(1280, 720)
    .jpeg({ quality: 80, mozjpeg: true })
    .toBuffer()
  await writeFile(join(heroOut, `${id}-${skin}.jpg`), full)
}
console.log(`${heroes.length} arka plan görseli indirildi (tam boy kostüm splash'ları).`)
if (heroOnly) process.exit(0)

// Draft ekranının pick kartları: yatay "centered" splash (1280×720; karakter ortada, geniş karta tamamı sığar;
// dikey yükleme görselinden yatay şerit kesince karakterin sadece gövdesi kalıyordu) → champions/splash/{id}.jpg.
// Data Dragon'da sürümsüz adreste (temel kostüm, _0).
const splashOut = join(out, 'splash')
await mkdir(splashOut, { recursive: true })
for (const champion of champions) {
  const key = keyOf(champion)
  const response = await fetchRetry(`${DDRAGON}/cdn/${version}/img/champion/${key}.png`)
  if (!response.ok) throw new Error(`${champion.id} (${key}): ${response.status}`)
  await writeFile(join(out, `${champion.id}.png`), Buffer.from(await response.arrayBuffer()))
  const splash = await fetchRetry(splashUrl(key))
  if (!splash.ok) throw new Error(`${champion.id} (${key}) splash: ${splash.status}`)
  // Kart en fazla ~240×76 (2x ekranda 480 genişlik): 640×360 yeter. Tam boy 170 tane 18 MB, küçülmüş ~5-7 MB.
  const small = await sharp(Buffer.from(await splash.arrayBuffer()))
    .resize(640, 360)
    .jpeg({ quality: 82, mozjpeg: true })
    .toBuffer()
  await writeFile(join(splashOut, `${champion.id}.jpg`), small)
}

// Hangi yamadan alındığı: Riot'un fan projesi politikası kaynağı belirtmeyi ister, güncellemede de işe yarar.
await writeFile(
  join(out, 'SOURCE.txt'),
  `Riot Games Data Dragon ${version}\n${DDRAGON}\nsplash/, hero/: ${DDRAGON}/cdn/img/champion/centered/{key}_0.jpg\n`,
)
console.log(`${champions.length} portre ve splash indirildi (Data Dragon ${version}).`)

// Maç ekranının haritası: Summoner's Rift (map11), aynı yamadan → web/public/map/map11.png (DESIGN.md §7).
const mapOut = join(root, 'web', 'public', 'map')
await mkdir(mapOut, { recursive: true })
const map = await fetch(`${DDRAGON}/cdn/${version}/img/map/map11.png`)
if (!map.ok) throw new Error(`map11: ${map.status}`)
await writeFile(join(mapOut, 'map11.png'), Buffer.from(await map.arrayBuffer()))

// Kule, nexus ve canavar ikonları: oyunun minimap ikonları (kule/nexus gri, takım rengini SVG filtresi verir; ejder,
// Elder, Herald, Baron maç oynatıcısının skor bandı ve log'u için, renkli). Data Dragon'da yok,
// CommunityDragon oyun dosyalarından çıkarıp yama klasörüyle yayınlar: aynı yamaya sabit (16.22.1 → 16.22).
const CDRAGON = 'https://raw.communitydragon.org'
const patch = version.split('.').slice(0, 2).join('.')
const MAP_ICONS = ['tower.png', 'inhibitor.png', 'nexus.png', 'dragon.png', 'dragon_elder.png', 'riftherald.png', 'baron.png']
for (const icon of MAP_ICONS) {
  const response = await fetch(`${CDRAGON}/${patch}/game/assets/ux/minimap/icons/${icon}`)
  if (!response.ok) throw new Error(`${icon}: ${response.status}`)
  await writeFile(join(mapOut, icon), Buffer.from(await response.arrayBuffer()))
}

await writeFile(
  join(mapOut, 'SOURCE.txt'),
  `map11.png: Riot Games Data Dragon ${version}\n${DDRAGON}\n` +
    `${MAP_ICONS.join(', ')}: CommunityDragon ${patch} (game/assets/ux/minimap/icons)\n${CDRAGON}\n`,
)
console.log(`Harita ve minimap ikonları indirildi (Data Dragon ${version}, CommunityDragon ${patch}).`)

// Rol ikonları (draft ekranı: filtreler ve yuvalar): istemcinin pozisyon ikonları, CommunityDragon, aynı yama.
// Bizim rol adımız → oyunun adı (mid = middle, bot = bottom, support = utility).
const rolesOut = join(root, 'web', 'public', 'roles')
await mkdir(rolesOut, { recursive: true })
const POSITIONS = { top: 'top', jungle: 'jungle', mid: 'middle', bot: 'bottom', support: 'utility' }
for (const [role, position] of Object.entries(POSITIONS)) {
  const url = `${CDRAGON}/${patch}/plugins/rcp-fe-lol-clash/global/default/assets/images/position-selector/positions/icon-position-${position}.png`
  const response = await fetch(url)
  if (!response.ok) throw new Error(`${role} ikonu: ${response.status}`)
  await writeFile(join(rolesOut, `${role}.png`), Buffer.from(await response.arrayBuffer()))
}
await writeFile(
  join(rolesOut, 'SOURCE.txt'),
  `CommunityDragon ${patch} (plugins/rcp-fe-lol-clash/.../position-selector/positions)\n${CDRAGON}\n`,
)
console.log(`Rol ikonları indirildi (CommunityDragon ${patch}).`)
