import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearSession, saveSession } from './api'
import { InspectionsWorkspace, type InspectionDetail } from './Inspections'

const vehicle = { id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', branchId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', branchName: 'Москва', vin: 'XTA210990Y0200001', make: 'Lada', model: 'Vesta', year: 2023, mileageKm: 12000, acceptedAt: '2026-07-12T10:00:00Z' }
const detail: InspectionDetail = {
  id: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', vehicleId: vehicle.id, branchId: vehicle.branchId,
  branchName: 'Москва', inspectorId: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', inspectorName: 'Анна', status: 'InProgress',
  mileageKm: 12000, templateName: 'Базовый осмотр', templateVersion: 1, revision: 1, needsReconditioning: false,
  version: 1, items: [{ id: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', key: 'body', category: 'Body', label: 'Кузов', isRequired: true, sortOrder: 1, result: 'Pending' }], defects: [],
}

function renderWorkspace(focusVehicle: typeof vehicle | null = null) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><InspectionsWorkspace focusVehicle={focusVehicle} onClearFocus={() => undefined} /></QueryClientProvider>)
}

beforeEach(() => saveSession({ accessToken: 'token', displayName: 'Анна', email: 'inspector@example.test', organizationName: 'Волга Авто', branchName: 'Москва' }))
afterEach(() => { clearSession(); vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('inspection workspace', () => {
  it('starts an inspection from the queue and tracks checklist progress', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString()
      if (url.endsWith('/api/inspections/queue')) return Response.json([vehicle])
      if (url.endsWith(`/api/vehicles/${vehicle.id}/inspections/start`)) return Response.json(detail)
      if (url.includes('/items/')) return Response.json({ ...detail, version: 2, items: [{ ...detail.items[0], result: 'Pass' }] })
      return new Response(JSON.stringify({ title: `Unexpected ${init?.method ?? 'GET'} ${url}` }), { status: 404 })
    }))
    renderWorkspace()

    await userEvent.click(await screen.findByRole('button', { name: 'Начать осмотр' }))
    expect(await screen.findByRole('heading', { name: 'Технический чек-лист' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Норма' }))
    expect(await screen.findByText('100%')).toBeInTheDocument()
  })

  it('shows a useful optimistic-concurrency conflict', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString()
      if (url.endsWith('/api/inspections/queue')) return Response.json([vehicle])
      if (url.endsWith(`/api/vehicles/${vehicle.id}/inspections/start`)) return Response.json(detail)
      if (url.includes('/items/')) return Response.json({ title: 'Осмотр изменён.' }, { status: 409 })
      return new Response(null, { status: 404 })
    }))
    renderWorkspace()

    await userEvent.click(await screen.findByRole('button', { name: 'Начать осмотр' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Норма' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Конфликт версий')
  })

  it('renders a completed revision as immutable and explains the next action', async () => {
    const completed = { ...detail, status: 'Completed' as const, needsReconditioning: true, completedAt: '2026-07-12T11:00:00Z', defects: [{ id: 'ffffffff-ffff-4fff-8fff-ffffffffffff', category: 'Brakes', title: 'Износ колодок', description: 'Критический износ', severity: 'Critical' as const, repairRequired: true, blocksPublication: false, blocksTestDrive: false, blocksSale: true, photos: [] }] }
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString()
      if (url.endsWith('/api/inspections/queue')) return Response.json([])
      if (url.endsWith(`/api/vehicles/${vehicle.id}/inspections`)) return Response.json([{ id: completed.id, vehicleId: vehicle.id, status: 'Completed', templateName: completed.templateName, templateVersion: 1, revision: 1, needsReconditioning: true, defectCount: 1 }])
      if (url.endsWith(`/api/inspections/${completed.id}`)) return Response.json(completed)
      return new Response(null, { status: 404 })
    }))
    renderWorkspace(vehicle)

    await userEvent.click(await screen.findByRole('button', { name: /Ревизия 1/ }))
    expect(await screen.findByText(/Результат зафиксирован/)).toBeInTheDocument()
    expect(screen.getByText(/раздел «Подготовка»/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Добавить дефект' })).not.toBeInTheDocument()
  })
})
