import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ReservationsWorkspace, type Reservation } from './Reservations'
import type { SalesOffer } from './Sales'

const snapshotId = '10000000-0000-4000-8000-000000000001'
const offer: SalesOffer = {
  id: '20000000-0000-4000-8000-000000000001', branchId: '30000000-0000-4000-8000-000000000001',
  customerId: '40000000-0000-4000-8000-000000000001', customerName: 'Ирина Клиент',
  leadId: '50000000-0000-4000-8000-000000000001', vehicleId: '60000000-0000-4000-8000-000000000001',
  vehicleName: 'Volkswagen Passat', revision: 1, status: 'Approved', basePriceAmount: 1_500_000,
  lineItemsAmount: 0, discountAmount: 0, finalPriceAmount: 1_500_000, costSnapshotAmount: 1_100_000,
  expectedMarginAmount: 400_000, minimumMarginAmount: 100_000, currency: 'RUB', belowMinimumMargin: false,
  requiresManagerApproval: false, autoApprovalDiscountLimit: 50_000,
  validUntil: new Date(Date.now() + 10 * 86_400_000).toISOString(), version: 2, lineItems: [], decisions: [],
  history: [], approvedSnapshot: { id: snapshotId, revision: 1, finalPriceAmount: 1_500_000,
    costSnapshotAmount: 1_100_000, expectedMarginAmount: 400_000, lineItemsJson: '[]',
    approvedAt: new Date().toISOString() },
}
const reservation: Reservation = {
  id: '70000000-0000-4000-8000-000000000001', branchId: offer.branchId, vehicleId: offer.vehicleId,
  vehicleName: offer.vehicleName, customerId: offer.customerId, customerName: offer.customerName,
  leadId: offer.leadId, approvedOfferSnapshotId: snapshotId, status: 'Active', depositStatus: 'NotRequired',
  depositAmount: 0, currency: 'RUB', createdAt: new Date().toISOString(), startsAt: new Date().toISOString(),
  expiresAt: new Date(Date.now() + 24 * 60 * 60_000).toISOString(), version: 1,
  history: [{ commandId: '80000000-0000-4000-8000-000000000001', operation: 'Created', occurredAt: new Date().toISOString() }],
}

function setup() {
  sessionStorage.setItem('dealeros.session', JSON.stringify({ accessToken: 'token', permissions: [
    'reservations.view', 'reservations.create', 'reservations.deposit', 'reservations.extend', 'reservations.cancel',
  ] }))
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><ReservationsWorkspace /></QueryClientProvider>)
}

describe('ReservationsWorkspace', () => {
  beforeEach(() => { sessionStorage.clear(); vi.restoreAllMocks() })

  it('creates a reservation only from an approved offer and shows its countdown', async () => {
    let sentSnapshot = ''
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/sales/offers')) return Response.json([offer])
      if (url.endsWith('/api/reservations') && init?.method === 'GET') return Response.json([])
      if (url.endsWith('/api/reservations') && init?.method === 'POST') {
        sentSnapshot = (JSON.parse(String(init.body)) as { approvedOfferSnapshotId: string }).approvedOfferSnapshotId
        return Response.json(reservation)
      }
      throw new Error(`Unexpected ${url} ${init?.method}`)
    })
    setup()
    await screen.findByRole('option', { name: /Ирина Клиент/ })
    await userEvent.selectOptions(await screen.findByLabelText('Утверждённое предложение для брони'), snapshotId)
    await userEvent.click(screen.getByRole('button', { name: 'Создать бронь' }))
    expect(await screen.findByText('Ирина Клиент')).toBeInTheDocument()
    expect(screen.getByText(/ч .*мин/)).toBeInTheDocument()
    expect(sentSnapshot).toBe(snapshotId)
  })

  it('shows a clear conflict when another manager wins the vehicle race', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/sales/offers')) return Response.json([offer])
      if (url.endsWith('/api/reservations') && init?.method === 'GET') return Response.json([])
      if (url.endsWith('/api/reservations') && init?.method === 'POST')
        return Response.json({ title: 'Автомобиль уже забронирован' }, { status: 409 })
      throw new Error(`Unexpected ${url}`)
    })
    setup()
    await screen.findByRole('option', { name: /Ирина Клиент/ })
    await userEvent.selectOptions(await screen.findByLabelText('Утверждённое предложение для брони'), snapshotId)
    await userEvent.click(screen.getByRole('button', { name: 'Создать бронь' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Конфликт бронирования')
  })

  it('registers the manual deposit through a separate command', async () => {
    const pending = { ...reservation, status: 'PendingDeposit', depositStatus: 'Pending', depositAmount: 100_000 }
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/sales/offers')) return Response.json([offer])
      if (url.endsWith('/api/reservations') && init?.method === 'GET') return Response.json([pending])
      if (url.endsWith(`/api/reservations/${reservation.id}/deposit`))
        return Response.json({ ...pending, status: 'Active', depositStatus: 'Received', version: 2 })
      throw new Error(`Unexpected ${url}`)
    })
    setup()
    await userEvent.click((await screen.findByText('Ирина Клиент')).closest('button')!)
    await userEvent.click(screen.getByRole('button', { name: 'Предоплата получена' }))
    expect(await screen.findByText('Получена')).toBeInTheDocument()
  })
})
