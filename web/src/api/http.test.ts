import { describe, expect, it } from 'vitest'
import { ApiError, readJson } from './http'

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('readJson', () => {
  it('başarılı cevabı çözer', async () => {
    await expect(readJson(json(200, { code: 'x' }))).resolves.toEqual({ code: 'x' })
  })

  it('iş hatasını kod ve sunucunun mesajıyla fırlatır', async () => {
    const error = await readJson(json(409, { error: 'lobby_full', message: 'Lobi dolu.' })).catch((e) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ code: 'lobby_full', message: 'Lobi dolu.', status: 409 })
  })

  // Gateway'in 429'u sözleşme gövdesi taşımaz (rate limiter boş gövde döner).
  it('gövdesiz hatada durum koduna göre Türkçe mesaj', async () => {
    const error = await readJson<never>(new Response(null, { status: 429 })).catch((e: ApiError) => e)

    expect(error).toMatchObject({ code: 'http_429', status: 429 })
    expect(error.message).toContain('Çok fazla istek')
  })
})
