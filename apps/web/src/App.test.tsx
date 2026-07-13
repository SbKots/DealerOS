import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { saveSession } from './api'

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('DealerOS intake workspace', () => {
  it('authenticates the demo employee and opens the intake form', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString()
      if (url.endsWith('/api/auth/login')) return Response.json({ accessToken: 'token', displayName: 'Анна', email: 'admin@volga-auto.demo', organizationName: 'Волга Авто', branchName: 'Москва' })
      if (url.endsWith('/api/branches')) return Response.json([{ id: '11111111-1111-4111-8111-111111111101', code: 'MSK', name: 'Москва' }])
      if (url.endsWith('/api/vehicles')) return Response.json([])
      return new Response(null, { status: 404 })
    }))
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={client}><App /></QueryClientProvider>)

    await userEvent.click(screen.getByRole('button', { name: 'Войти в DealerOS' }))

    expect(await screen.findByRole('heading', { name: 'Приём автомобиля' })).toBeInTheDocument()
    expect(screen.getByLabelText('VIN')).toBeInTheDocument()
  })

  it('returns to login when the server rejects an expired session', async () => {
    saveSession({ accessToken: 'expired', displayName: 'Анна', email: 'admin@volga-auto.demo', organizationName: 'Волга Авто', branchName: 'Москва' })
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ title: 'Сессия истекла' }, { status: 401 })))
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={client}><App /></QueryClientProvider>)

    expect(await screen.findByRole('heading', { name: 'Войдите в рабочее пространство' })).toBeInTheDocument()
    expect(sessionStorage.getItem('dealeros.session')).toBeNull()
  })
})
