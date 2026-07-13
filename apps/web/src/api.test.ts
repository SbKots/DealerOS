import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from './api'

describe('api', () => {
  beforeEach(() => { sessionStorage.clear(); vi.restoreAllMocks() })

  it('returns null for an authenticated 204 No Content response', async () => {
    sessionStorage.setItem('dealeros.session', JSON.stringify({ accessToken: 'token' }))
    let authorization: string | null = null
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (_input, init) => {
      authorization = new Headers(init?.headers).get('Authorization')
      return new Response(null, { status: 204 })
    })

    await expect(api<null>('/api/optional-resource')).resolves.toBeNull()
    expect(authorization).toBe('Bearer token')
  })

  it('returns null for a successful 200 response with an empty body', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('', { status: 200 }))

    await expect(api<null>('/api/optional-resource')).resolves.toBeNull()
  })
})
