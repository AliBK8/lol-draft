import type { ApiErrorBody, Champion, Format, LobbyResponse, Ticket } from './types'

/**
 * Sunucunun iş hatası (gövde: { error, message }) ya da ağ/HTTP hatası. `code` istemcinin dallandığı değer
 * (ör. lobby_full, lobby_not_found); `message` kullanıcıya gösterilebilir Türkçe metin.
 */
export class ApiError extends Error {
  readonly code: string
  readonly status: number

  constructor(code: string, message: string, status: number) {
    super(message)
    this.code = code
    this.status = status
  }
}

/** Cevabı çözer; başarısızsa ApiError. Gövde sözleşmeye uymuyorsa (ör. gateway'in 429'u) durum koduna göre metin. */
export async function readJson<T>(response: Response): Promise<T> {
  if (response.ok) return (await response.json()) as T

  const body = (await response.json().catch(() => null)) as Partial<ApiErrorBody> | null
  if (body?.error && body.message) throw new ApiError(body.error, body.message, response.status)
  throw new ApiError(`http_${response.status}`, fallbackMessage(response.status), response.status)
}

function fallbackMessage(status: number): string {
  if (status === 429) return 'Çok fazla istek. Biraz bekleyip tekrar dene.'
  if (status === 401) return 'Oturumun geçersiz ya da süresi dolmuş.'
  if (status >= 500) return 'Sunucuya ulaşılamadı. Biraz sonra tekrar dene.'
  return 'Beklenmeyen bir hata oldu.'
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(path, init)
  } catch {
    throw new ApiError('network', 'Sunucuya ulaşılamadı. Bağlantını kontrol et.', 0)
  }
  return readJson<T>(response)
}

export const api = {
  createLobby: (format: Format, fearless: boolean) =>
    request<Ticket>('/api/lobbies', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ format, fearless }),
    }),

  joinLobby: (code: string) =>
    request<Ticket>(`/api/lobbies/${encodeURIComponent(code)}/join`, { method: 'POST' }),

  getLobby: (code: string, token: string) =>
    request<LobbyResponse>(`/api/lobbies/${encodeURIComponent(code)}`, {
      headers: { Authorization: `Bearer ${token}` },
    }),

  getChampions: () => request<{ champions: Champion[] }>('/api/champions').then((r) => r.champions),
}
