import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { saveSession } from './api'

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('DealerOS intake workspace', () => {
  it('authenticates the demo employee, opens the overview and keeps intake accessible', async () => {
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

    expect(await screen.findByRole('heading', { name: 'Обзор' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('navigation', { name: 'Разделы' }).querySelector('button[title="Приёмка"]') ?? screen.getByRole('button', { name: 'Приёмка' }))
    expect(await screen.findByRole('heading', { name: 'Приёмка и реестр' })).toBeInTheDocument()
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

  it('keeps viewer navigation and actions permission-aware', async () => {
    saveSession({ accessToken: 'viewer-token', displayName: 'Виктор Наблюдатель', email: 'viewer@volga-auto.demo', organizationName: 'Волга Авто', branchName: 'Москва', permissions: ['vehicles.read'] })
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString()
      if (url.endsWith('/api/branches')) return Response.json([{ id: '11111111-1111-4111-8111-111111111101', code: 'MSK', name: 'Москва' }])
      if (url.endsWith('/api/vehicles')) return Response.json([])
      return new Response(null, { status: 404 })
    }))
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={client}><App /></QueryClientProvider>)

    const navigation = await screen.findByRole('navigation', { name: 'Разделы' })
    expect(navigation).toHaveTextContent('Обзор')
    expect(navigation).toHaveTextContent('Приёмка')
    expect(screen.queryByRole('button', { name: 'Осмотры' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Сделки' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Новое поступление' })).not.toBeInTheDocument()
  })
})
