// Yürünebilir harita ızgarası (UYGULAMA.md §8.26): public/map/map11.png → src/match/navgrid.json.
// Görselde duvarlar siyah, geri kalan her yer (koridor, orman yolu, nehir, üs) yürünebilir. Harita 600 birim,
// hücre 5 birim → 120×120 ızgara. Bir hücre, kapladığı piksellerin yarısından fazlası siyah değilse yürünebilir.
// Bağımlılık yok: PNG (8 bit RGB, taramasız) zlib ile burada çözülür. Kullanım: npm run navgrid
import { readFileSync, writeFileSync } from 'node:fs'
import { inflateSync } from 'node:zlib'

const SOURCE = new URL('../public/map/map11.png', import.meta.url)
const TARGET = new URL('../src/match/navgrid.json', import.meta.url)
const MAP_SIZE = 600
const CELL = 5
const N = MAP_SIZE / CELL
/** Bu parlaklığın (en büyük kanal) altı duvar sayılır; duvar siyahı 0-10, en koyu yol ~60. */
const WALL_MAX = 30

function decodePng(buffer) {
  const width = buffer.readUInt32BE(16)
  const height = buffer.readUInt32BE(20)
  if (buffer[24] !== 8 || buffer[25] !== 2 || buffer[28] !== 0) throw new Error('Beklenen: 8 bit RGB, taramasız PNG')
  // IDAT parçaları birleşip tek zlib akışı olur.
  const chunks = []
  for (let i = 8; i < buffer.length; ) {
    const length = buffer.readUInt32BE(i)
    const type = buffer.toString('ascii', i + 4, i + 8)
    if (type === 'IDAT') chunks.push(buffer.subarray(i + 8, i + 8 + length))
    i += 12 + length
  }
  const raw = inflateSync(Buffer.concat(chunks))
  const bpp = 3
  const stride = width * bpp
  const pixels = Buffer.alloc(height * stride)
  // Her satırın başında filtre türü; satır bir öncekine göre çözülür (PNG standardı: None, Sub, Up, Average, Paeth).
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)]
    for (let x = 0; x < stride; x++) {
      const value = raw[y * (stride + 1) + 1 + x]
      const a = x >= bpp ? pixels[y * stride + x - bpp] : 0
      const b = y > 0 ? pixels[(y - 1) * stride + x] : 0
      const c = x >= bpp && y > 0 ? pixels[(y - 1) * stride + x - bpp] : 0
      let predictor = 0
      if (filter === 1) predictor = a
      else if (filter === 2) predictor = b
      else if (filter === 3) predictor = (a + b) >> 1
      else if (filter === 4) {
        const p = a + b - c
        const pa = Math.abs(p - a)
        const pb = Math.abs(p - b)
        const pc = Math.abs(p - c)
        predictor = pa <= pb && pa <= pc ? a : pb <= pc ? b : c
      }
      pixels[y * stride + x] = (value + predictor) & 0xff
    }
  }
  return { width, height, pixels }
}

const { width, height, pixels } = decodePng(readFileSync(SOURCE))
const rows = []
for (let gy = 0; gy < N; gy++) {
  let row = ''
  for (let gx = 0; gx < N; gx++) {
    // Hücrenin kapladığı piksel aralığı (görsel 512, harita 600: ölçek 512/600).
    const x0 = Math.floor((gx * CELL * width) / MAP_SIZE)
    const x1 = Math.max(x0 + 1, Math.floor(((gx + 1) * CELL * width) / MAP_SIZE))
    const y0 = Math.floor((gy * CELL * height) / MAP_SIZE)
    const y1 = Math.max(y0 + 1, Math.floor(((gy + 1) * CELL * height) / MAP_SIZE))
    let open = 0
    let total = 0
    for (let y = y0; y < y1; y++) {
      for (let x = x0; x < x1; x++) {
        const i = (y * width + x) * 3
        if (Math.max(pixels[i], pixels[i + 1], pixels[i + 2]) > WALL_MAX) open++
        total++
      }
    }
    row += open * 2 > total ? '.' : '#'
  }
  rows.push(row)
}

writeFileSync(TARGET, `${JSON.stringify({ cell: CELL, size: N, rows }, null, 1)}\n`)
const walkable = rows.join('').split('').filter((c) => c === '.').length
console.log(`navgrid.json: ${N}×${N}, yürünebilir %${((walkable / (N * N)) * 100).toFixed(1)}`)
