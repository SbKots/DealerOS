import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { DealsWorkspace, type Deal } from './Deals'
import type { Reservation } from './Reservations'

const reservation: Reservation = {
  id: '10000000-0000-4000-8000-000000000001', branchId: '20000000-0000-4000-8000-000000000001',
  vehicleId: '30000000-0000-4000-8000-000000000001', vehicleName: 'Volkswagen Passat',
  customerId: '40000000-0000-4000-8000-000000000001', customerName: 'Ирина Клиент',
  leadId: '50000000-0000-4000-8000-000000000001', approvedOfferSnapshotId: '60000000-0000-4000-8000-000000000001',
  status: 'Active', depositStatus: 'NotRequired', depositAmount: 0, currency: 'RUB',
  createdAt: new Date().toISOString(), startsAt: new Date().toISOString(),
  expiresAt: new Date(Date.now() + 86_400_000).toISOString(), version: 1, history: [],
}
const deal: Deal = {
  id: '70000000-0000-4000-8000-000000000001', branchId: reservation.branchId, reservationId: reservation.id,
  approvedOfferSnapshotId: reservation.approvedOfferSnapshotId, customerId: reservation.customerId,
  leadId: reservation.leadId, vehicleId: reservation.vehicleId, customerName: reservation.customerName,
  vehicleSnapshotJson: JSON.stringify({ make: 'Volkswagen', model: 'Passat', vin: 'WVWZZZ1JZXW654321' }),
  lineItemsJson: '[]', status: 'Draft', basePriceAmount: 1_500_000, lineItemsAmount: 0,
  discountAmount: 0, finalTotalAmount: 1_500_000, costSnapshotAmount: 1_100_000,
  expectedMarginAmount: 400_000, currency: 'RUB', receivedTotal: 0, refundedTotal: 0, netPaid: 0,
  balance: 1_500_000, createdAt: new Date().toISOString(), version: 1, payments: [], documents: [], history: [],
}

function setup() {
  sessionStorage.setItem('dealeros.session', JSON.stringify({ accessToken: 'token', permissions: [
    'deals.view', 'deals.create', 'deals.edit', 'deals.payments', 'deals.documents', 'deals.handover', 'deals.cancel',
  ] }))
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><DealsWorkspace /></QueryClientProvider>)
}

describe('DealsWorkspace', () => {
  beforeEach(() => { sessionStorage.clear(); vi.restoreAllMocks() })

  it('creates a deal only from an active reservation', async () => {
    let sentReservation = ''
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/reservations')) return Response.json([reservation])
      if (url.endsWith('/api/deals') && init?.method === 'GET') return Response.json([])
      if (url.endsWith('/api/deals') && init?.method === 'POST') {
        sentReservation = (JSON.parse(String(init.body)) as { reservationId: string }).reservationId
        return Response.json(deal)
      }
      throw new Error(`Unexpected ${url}`)
    })
    setup()
    await screen.findByRole('option', { name: /Ирина Клиент/ })
    await userEvent.selectOptions(screen.getByLabelText('Активное бронирование для сделки'), reservation.id)
    await userEvent.click(screen.getByRole('button', { name: 'Создать сделку' }))
    expect(await screen.findByText('Ирина Клиент')).toBeInTheDocument()
    expect(sentReservation).toBe(reservation.id)
  })

  it('shows server blockers and records a manual payment as an append-only command', async () => {
    const awaiting = { ...deal, status: 'AwaitingPayment', version: 2 }
    let paymentBody: { amount?: number; kind?: string } = {}
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/reservations')) return Response.json([])
      if (url.endsWith('/api/deals') && init?.method === 'GET') return Response.json([awaiting])
      if (url.endsWith(`/api/deals/${deal.id}/payments`)) {
        paymentBody = JSON.parse(String(init?.body)) as typeof paymentBody
        return Response.json({ ...awaiting, version: 3, receivedTotal: 1_500_000, netPaid: 1_500_000, balance: 0,
          payments: [{ id: '80000000-0000-4000-8000-000000000001', commandId: '90000000-0000-4000-8000-000000000001', kind: 'Payment', status: 'Received', amount: 1_500_000, currency: 'RUB', manualReference: 'DEMO', reason: 'Оплата', occurredAt: new Date().toISOString(), recordedAt: new Date().toISOString() }] })
      }
      throw new Error(`Unexpected ${url}`)
    })
    setup()
    await userEvent.click((await screen.findByText('Ирина Клиент')).closest('button')!)
    expect(screen.getByText(/Остаток оплаты/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Зарегистрировать оплату' }))
    expect(await screen.findByText('Оплата · Получена')).toBeInTheDocument()
    expect(paymentBody).toMatchObject({ amount: 1_500_000, kind: 'Payment' })
    expect(screen.getByText('Нет договора купли-продажи')).toBeInTheDocument()
  })

  it('renders a completed sale as read-only with immutable document revisions', async () => {
    const completed: Deal = { ...deal, status: 'Completed', version: 8, receivedTotal: 1_500_000,
      netPaid: 1_500_000, balance: 0, completedAt: new Date().toISOString(), documents: [
        { id: 'a0000000-0000-4000-8000-000000000001', commandId: 'b0000000-0000-4000-8000-000000000001',
          type: 'SaleContract', number: 'DOS-SC-R1', templateName: 'Demo', templateVersion: 1, revision: 1,
          sha256: 'a'.repeat(64), sizeBytes: 1000, generatedAt: new Date().toISOString() },
        { id: 'c0000000-0000-4000-8000-000000000001', commandId: 'd0000000-0000-4000-8000-000000000001',
          type: 'HandoverAct', number: 'DOS-HA-R1', templateName: 'Demo', templateVersion: 1, revision: 1,
          sha256: 'b'.repeat(64), sizeBytes: 1000, generatedAt: new Date().toISOString() },
      ] }
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input)
      if (url.endsWith('/api/reservations')) return Response.json([])
      if (url.endsWith('/api/deals')) return Response.json([completed])
      throw new Error(`Unexpected ${url}`)
    })
    setup()
    await userEvent.click((await screen.findByText('Ирина Клиент')).closest('button')!)
    expect(screen.getByText('Автомобиль продан')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Скачать PDF' })).toHaveLength(2)
    expect(screen.queryByRole('button', { name: 'Новая ревизия' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Отменить сделку' })).not.toBeInTheDocument()
  })
})
