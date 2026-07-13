import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { QualityListingsWorkspace, type Listing, type QualityCheck, type QualityQueue } from './QualityListings'

function setup(permissions: string[]) {
  sessionStorage.setItem('dealeros.session', JSON.stringify({ accessToken: 'token', permissions }))
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><QualityListingsWorkspace /></QueryClientProvider>)
}

describe('QualityListingsWorkspace', () => {
  beforeEach(() => { sessionStorage.clear(); vi.restoreAllMocks() })

  it('creates an immutable QC attempt and exposes the explicit pass command', async () => {
    const queue: QualityQueue = { executionId: '10000000-0000-4000-8000-000000000001', vehicleId: '20000000-0000-4000-8000-000000000001', vin: 'XTA210990Y2765432', make: 'Lada', model: 'Vesta', completedAt: '2026-07-14T10:00:00Z', actualTotalAmount: 25000, currency: 'RUB', workOrders: [{ id: '30000000-0000-4000-8000-000000000001', sourceDefectId: '40000000-0000-4000-8000-000000000001', title: 'Ремонт бампера' }] }
    const draft: QualityCheck = { id: '50000000-0000-4000-8000-000000000001', vehicleId: queue.vehicleId, executionId: queue.executionId, revision: 1, status: 'Draft', checklistSnapshotJson: '{"version":1,"mandatoryWorksCompleted":true}', version: 1, observations: [] }
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/quality-checks/queue')) return Response.json([queue])
      if (url.endsWith(`/api/quality-checks/executions/${queue.executionId}`)) return Response.json(draft)
      if (url.endsWith(`/api/quality-checks/${draft.id}/pass`) && init?.method === 'POST') return Response.json({ ...draft, status: 'Passed', version: 2 })
      throw new Error(`Unexpected ${url}`)
    })
    setup(['quality.view', 'quality.create', 'quality.decide'])
    await userEvent.click(await screen.findByRole('button', { name: 'Открыть QC' }))
    expect(await screen.findByText(/mandatoryWorksCompleted/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Подтвердить ReadyForSale' }))
    expect(await screen.findByText(/QC rev. 1 · Passed/)).toBeInTheDocument()
  })

  it('shows a Ready listing as read-only and distinguishes a confirmed publication', async () => {
    const vehicle = { id: '20000000-0000-4000-8000-000000000001', branchId: '60000000-0000-4000-8000-000000000001', branchName: 'Центр', vin: 'XTA210990Y2765432', make: 'Lada', model: 'Vesta', year: 2023, mileageKm: 20000, plannedPurchaseAmount: 1000000, currency: 'RUB', status: 'ReadyForSale', createdAt: '2026-07-14T10:00:00Z', version: 5 }
    const listing: Listing = { id: '70000000-0000-4000-8000-000000000001', vehicleId: vehicle.id, revision: 1, status: 'Ready', vehicleMake: 'Lada', vehicleModel: 'Vesta', vehicleYear: 2023, mileageKm: 20000, equipment: 'Климат', advantages: 'История', conditionDescription: 'Состояние раскрыто', publicPriceAmount: 1400000, currency: 'RUB', templateName: 'DealerOS Default', templateVersion: 1, version: 3, publications: [{ id: '80000000-0000-4000-8000-000000000001', channel: 'demo-channel', status: 'Published', externalId: 'DEMO-1' }] }
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input)
      if (url.endsWith('/api/vehicles')) return Response.json([vehicle])
      if (url.endsWith(`/api/vehicles/${vehicle.id}/media`)) return Response.json([])
      if (url.endsWith(`/api/vehicles/${vehicle.id}/listing`)) return Response.json(listing)
      throw new Error(`Unexpected ${url}`)
    })
    setup(['listings.view', 'listings.edit', 'listings.publish'])
    await userEvent.click(await screen.findByRole('button', { name: 'Подготовить объявление' }))
    expect(await screen.findByLabelText('Публичная цена')).toBeDisabled()
    expect(screen.getByText('demo-channel').closest('p')).toHaveTextContent('Published')
  })
})
