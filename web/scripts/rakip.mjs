// Geliştirme rakibi (DESIGN.md §8.18): tek başına test için otomatik oynayan ikinci oyuncu. Gerçek bir istemci gibi
// gateway'den geçer (REST + SignalR, token, rate limit); sunucuya özel bir şey eklemez. Oyunun parçası değil.
//
//   npm run rakip -- <davet linki ya da kod>     lobiye katılır (B koltuğu) ve oynar
//   npm run rakip -- --yeni bo3 [--fearless]     lobi açar (A koltuğu), linki yazar, rakibi bekler ve oynar
//   --gateway http://localhost:5000              (varsayılan)
//
// Oyun tarzı: hazır der; ban'da havuzdan rastgele; pick'te sıradaki yuvanın (1. pick top, 2. orman…) rolündeki
// rastgele bir şampiyon, yoksa herhangi biri; swap'ta dizilimi olduğu gibi kilitler. Seri bitince çıkar.
import * as signalR from '@microsoft/signalr'

const ROLES = ['top', 'jungle', 'mid', 'bot', 'support']
const ORDER = [
  ...['blue', 'red', 'blue', 'red', 'blue', 'red'].map((side) => ({ side, kind: 'ban' })),
  ...['blue', 'red', 'red', 'blue', 'blue', 'red'].map((side) => ({ side, kind: 'pick' })),
  ...['red', 'blue', 'red', 'blue'].map((side) => ({ side, kind: 'ban' })),
  ...['red', 'blue', 'blue', 'red'].map((side) => ({ side, kind: 'pick' })),
]

const args = process.argv.slice(2)
const option = (name) => {
  const i = args.indexOf(name)
  return i >= 0 ? args[i + 1] : undefined
}
const gateway = option('--gateway') ?? 'http://localhost:5000'
const delayMs = Number(option('--gecikme') ?? 700) // insan gibi: hamleler arası bekleme (hub limiti sn'de 5)

async function http(path, init) {
  const response = await fetch(`${gateway}${path}`, init)
  const body = await response.json().catch(() => null)
  if (!response.ok) throw new Error(`${path}: ${response.status} ${body?.error ?? ''} ${body?.message ?? ''}`)
  return body
}

let ticket
if (args.includes('--yeni')) {
  const format = option('--yeni') ?? 'bo3'
  ticket = await http('/api/lobbies', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ format, fearless: args.includes('--fearless') }),
  })
  console.log(`Lobi açıldı (${format}). Davet linki: http://localhost:5173/l/${ticket.code}`)
} else {
  const code = args.find((a) => /[A-Za-z0-9_-]{22}/.test(a))?.match(/[A-Za-z0-9_-]{22}/)[0]
  if (!code) {
    console.error('Kullanım: npm run rakip -- <davet linki>  |  npm run rakip -- --yeni bo3')
    process.exit(2)
  }
  ticket = await http(`/api/lobbies/${code}/join`, { method: 'POST' })
}
const seat = ticket.seat
const log = (text) => console.log(`[rakip ${seat}] ${text}`)
log(`koltuk ${seat}`)

const { champions } = await http('/api/champions')
const random = (list) => list[Math.floor(Math.random() * list.length)]

const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${gateway}/hubs/draft`, { accessTokenFactory: () => ticket.token })
  .withAutomaticReconnect()
  .configureLogging(signalR.LogLevel.Warning)
  .build()

let latest = null
const done = new Set() // aynı hamleyi iki kez göndermemek için: "maç:adım" anahtarları
let busy = false

async function act(key, method, ...rest) {
  if (done.has(key)) return
  done.add(key)
  busy = true
  await new Promise((resolve) => setTimeout(resolve, delayMs))
  try {
    const result = await connection.invoke(method, ...rest, crypto.randomUUID())
    if (!result.ok) log(`${method} reddedildi: ${result.error} (${result.message})`)
  } finally {
    busy = false
    if (latest) void decide(latest) // beklerken gelen durum
  }
}

async function decide(state) {
  if (busy) return
  if (state.winner) {
    log(`seri bitti: kazanan ${state.winner}${state.endReason === 'forfeit' ? ' (hükmen)' : ''}, skor A ${state.winsA} - ${state.winsB} B`)
    await connection.stop()
    process.exit(0)
  }
  if (!state.isFull) return

  const game = state.gameNumber
  const side = state.blueTeam === seat ? 'blue' : 'red'
  const draft = state.draft

  if (!draft) {
    if (!state.ready.includes(seat)) await act(`${game}:ready`, 'Ready')
    return
  }

  if (draft.phase === 'swap') {
    const locked = side === 'blue' ? draft.blueLocked : draft.redLocked
    if (!locked) await act(`${game}:lock`, 'LockLineup')
    return
  }

  if (draft.turn !== side || !draft.step) return
  const step = draft.actions.length
  const taken = new Set([...draft.actions.map((a) => a.champion).filter(Boolean), ...draft.fearlessLocks])
  const free = champions.filter((c) => !taken.has(c.id))

  if (draft.step === 'ban') {
    await act(`${game}:${step}`, 'Ban', random(free).id)
    return
  }
  // Kaçıncı pick'im: o yuvanın rolünden seç (pick sırası = yuva sırası).
  const myPicks = ORDER.slice(0, step).filter((s) => s.side === side && s.kind === 'pick').length
  const role = ROLES[myPicks]
  const pick = random(free.filter((c) => c.roles.includes(role))) ?? random(free)
  await act(`${game}:${step}`, 'Pick', pick.id)
}

connection.on('LobbyUpdated', (state) => {
  if (latest && state.revision <= latest.revision) return // eski durum (§8.12)
  if (!latest || latest.draft?.phase !== state.draft?.phase) {
    log(state.draft ? `maç ${state.gameNumber}: ${state.draft.phase}` : state.isFull ? 'lobide' : 'rakip bekleniyor')
  }
  latest = state
  void decide(state)
})
connection.on('MatchReady', (match) => log(`maç ${match.gameNumber} oynandı → ${gateway.replace(/:\d+$/, ':5173')}/m/${match.id}`))
connection.on('LobbyClosed', () => {
  log('lobi kapandı')
  process.exit(0)
})

await connection.start()
log('bağlandı')
