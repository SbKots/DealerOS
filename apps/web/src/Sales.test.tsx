import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Lead } from './Crm'
import { SalesWorkspace, type SalesOffer, type Visit } from './Sales'

const lead: Lead = { id: '10000000-0000-4000-8000-000000000001', branchId: '20000000-0000-4000-8000-000000000001', customerId: '30000000-0000-4000-8000-000000000001', customerName: 'Иван Покупатель', vehicleId: '40000000-0000-4000-8000-000000000001', source: 'demo', assignedManagerUserId: '50000000-0000-4000-8000-000000000001', assignedManagerName: 'Марина', status: 'Qualified', createdAt: '2026-07-14T10:00:00Z', firstResponseDueAt: '2026-07-14T10:30:00Z', firstResponseAt: '2026-07-14T10:10:00Z', slaBreached: false, version: 5, activities: [], history: [] }
const visit: Visit = { id: '60000000-0000-4000-8000-000000000001', branchId: lead.branchId, customerId: lead.customerId, customerName: lead.customerName, leadId: lead.id, vehicleId: lead.vehicleId!, vehicleName: 'Lada XRAY', responsibleUserId: lead.assignedManagerUserId!, responsibleName: 'Марина', status: 'Scheduled', startsAt: '2026-07-15T10:00:00Z', endsAt: '2026-07-15T11:00:00Z', includesTestDrive: true, driverDocumentsChecked: false, incidentOccurred: false, version: 1, history: [] }
const offerBase: SalesOffer = { id: '70000000-0000-4000-8000-000000000001', branchId: lead.branchId, customerId: lead.customerId, customerName: lead.customerName, leadId: lead.id, vehicleId: lead.vehicleId!, vehicleName: 'Lada XRAY', revision: 1, status: 'Submitted', basePriceAmount: 1350000, lineItemsAmount: 20000, discountAmount: 100000, finalPriceAmount: 1270000, costSnapshotAmount: 990000, expectedMarginAmount: 280000, minimumMarginAmount: 100000, currency: 'RUB', belowMinimumMargin: false, requiresManagerApproval: true, autoApprovalDiscountLimit: 50000, validUntil: '2026-07-25T10:00:00Z', version: 2, lineItems: [], decisions: [], history: [] }

function setup(permissions: string[]) {
  sessionStorage.setItem('dealeros.session', JSON.stringify({ accessToken: 'token', permissions }))
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><SalesWorkspace /></QueryClientProvider>)
}

describe('SalesWorkspace', () => {
  beforeEach(() => { sessionStorage.clear(); vi.restoreAllMocks() })

  it('runs the explicit arrival and safe test-drive checkout commands', async () => {
    let current = visit
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/crm/leads')) return Response.json([lead])
      if (url.endsWith('/api/sales/visits') && init?.method === 'GET') return Response.json([current])
      if (url.endsWith(`/api/sales/visits/${visit.id}/arrive`)) { current = { ...current, status: 'Arrived', version: 2 }; return Response.json(current) }
      if (url.endsWith(`/api/sales/visits/${visit.id}/test-drive/check-out`)) { current = { ...current, driverDocumentsChecked: true, checkedOutAt: '2026-07-15T10:05:00Z', odometerOutKm: 46250, version: 3 }; return Response.json(current) }
      throw new Error(`Unexpected ${url}`)
    })
    setup(['sales.visits.view', 'sales.visits.create', 'sales.visits.edit', 'sales.visits.complete'])
    await userEvent.click((await screen.findByText('Lada XRAY · Марина')).closest('button')!)
    await userEvent.click(screen.getByRole('button', { name: 'Клиент прибыл' }))
    expect(await screen.findByText('Проверка без хранения номера документа')).toBeInTheDocument()
    await userEvent.clear(screen.getByLabelText('Пробег до'))
    await userEvent.type(screen.getByLabelText('Пробег до'), '46250')
    await userEvent.click(screen.getByRole('button', { name: 'Выдать на тест-драйв' }))
    expect(await screen.findByRole('button', { name: 'Принять автомобиль' })).toBeInTheDocument()
  })

  it('renders totals only from the server preview and explains manager approval', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/crm/leads')) return Response.json([lead])
      if (url.endsWith('/api/sales/offers') && init?.method === 'GET') return Response.json([])
      if (url.endsWith('/api/sales/offers/preview')) return Response.json(offerBase)
      throw new Error(`Unexpected ${url}`)
    })
    setup(['sales.offers.view', 'sales.offers.create'])
    await screen.findByRole('option', { name: lead.customerName })
    await userEvent.selectOptions(screen.getByLabelText('Lead для предложения'), lead.id)
    await userEvent.clear(screen.getByLabelText('Скидка'))
    await userEvent.type(screen.getByLabelText('Скидка'), '100000')
    await userEvent.click(screen.getByRole('button', { name: 'Рассчитать на сервере' }))
    expect(await screen.findByText(/1\s270\s000/)).toBeInTheDocument()
    expect(screen.getByText(/Требуется согласование руководителя/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Создать предложение' })).toBeEnabled()
  })

  it('requires an explicit reason and shows the immutable approved snapshot', async () => {
    let current = offerBase
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/crm/leads')) return Response.json([lead])
      if (url.endsWith('/api/sales/offers') && init?.method === 'GET') return Response.json([current])
      if (url.endsWith(`/api/sales/offers/${offerBase.id}/decision`)) {
        current = { ...current, status: 'Approved', version: 3, approvedSnapshot: { id: '80000000-0000-4000-8000-000000000001', revision: 1, finalPriceAmount: 1270000, costSnapshotAmount: 990000, expectedMarginAmount: 280000, lineItemsJson: '[]', approvedAt: '2026-07-14T12:00:00Z' } }
        return Response.json(current)
      }
      throw new Error(`Unexpected ${url}`)
    })
    setup(['sales.offers.view', 'sales.offers.approve'])
    await userEvent.click((await screen.findByText('Lada XRAY · рев. 1')).closest('button')!)
    expect(screen.getByLabelText('Причина решения по Offer')).toHaveValue('Экономика и полномочия проверены')
    await userEvent.click(screen.getByRole('button', { name: 'Утвердить предложение' }))
    expect(await screen.findByText('Утверждённая неизменяемая версия')).toBeInTheDocument()
  })

  it('adds, removes and saves transparent lines in a draft offer', async () => {
    let savedLines: SalesOffer['lineItems'] = []
    const draft: SalesOffer = { ...offerBase, status: 'Draft', version: 1, discountAmount: 0, lineItems: [{ id: '90000000-0000-4000-8000-000000000001', category: 'Equipment', name: 'Старый комплект', amount: 10_000, currency: 'RUB' }] }
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/crm/leads')) return Response.json([lead])
      if (url.endsWith('/api/sales/offers') && init?.method === 'GET') return Response.json([draft])
      if (url.endsWith(`/api/sales/offers/${draft.id}`) && init?.method === 'PUT') {
        const body = JSON.parse(String(init.body)) as { lineItems: SalesOffer['lineItems'] }
        savedLines = body.lineItems
        return Response.json({ ...draft, version: 2, lineItems: savedLines })
      }
      throw new Error(`Unexpected ${url}`)
    })
    setup(['sales.offers.view', 'sales.offers.edit'])
    await userEvent.click((await screen.findByText('Lada XRAY · рев. 1')).closest('button')!)
    await userEvent.click(screen.getByRole('button', { name: 'Добавить строку' }))
    await userEvent.type(screen.getByLabelText('Название строки 2'), 'Новый комплект')
    await userEvent.clear(screen.getByLabelText('Стоимость строки 2'))
    await userEvent.type(screen.getByLabelText('Стоимость строки 2'), '25000')
    await userEvent.click(screen.getByRole('button', { name: 'Удалить строку 1' }))
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить черновик' }))
    expect(savedLines).toHaveLength(1)
    expect(savedLines[0]).toMatchObject({ name: 'Новый комплект', amount: 25_000 })
  })
})
