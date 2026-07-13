import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearSession, saveSession } from './api'
import { ReconditioningWorkspace, type ReconditioningPlan, type ReconditioningQueueVehicle } from './Reconditioning'

const vehicle: ReconditioningQueueVehicle = {
  vehicleId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', branchId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
  branchName: 'Москва', vin: 'XTA210990Y0200002', make: 'Lada', model: 'XRAY', year: 2022,
  stockNumber: 'MSK-2026-DEMO', inspectionId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
  inspectionCompletedAt: '2026-07-13T10:00:00Z', mandatoryDefectCount: 1, hasActivePlan: false,
}
const plan: ReconditioningPlan = {
  id: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', vehicleId: vehicle.vehicleId, branchId: vehicle.branchId,
  branchName: vehicle.branchName, vin: vehicle.vin, make: vehicle.make, model: vehicle.model,
  stockNumber: vehicle.stockNumber, status: 'Draft', revision: 1, sourceInspectionId: vehicle.inspectionId,
  createdByUserId: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', createdByName: 'Елена Подготовка', workCount: 1,
  version: 1, createdAt: '2026-07-13T10:10:00Z', updatedAt: '2026-07-13T10:10:00Z',
  budgets: [{ currency: 'RUB', laborAmount: 32000, partsAmount: 0, totalAmount: 32000 }],
  works: [{ id: 'ffffffff-ffff-4fff-8fff-ffffffffffff', sourceDefectId: '11111111-1111-4111-8111-111111111111',
    sourceDefectTitle: 'Повреждение бампера', sourceDefectDescription: 'Трещина', sourceDefectSeverity: 'Major',
    title: 'Устранить повреждение', description: 'Ремонт и окраска', category: 'Body', priority: 'High',
    isMandatory: true, executorType: 'Internal', executorName: 'Сервисный участок', estimatedLaborAmount: 32000,
    estimatedPartsAmount: 0, currency: 'RUB', estimatedDurationDays: 1 }],
  omissions: [], decisions: [], approvedBudgetSnapshots: [],
  history: [{ id: '12111111-1111-4111-8111-111111111111', toStatus: 'Draft', actorName: 'Елена Подготовка', occurredAt: '2026-07-13T10:10:00Z' }],
}

function renderWorkspace() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}><ReconditioningWorkspace /></QueryClientProvider>)
}

beforeEach(() => saveSession({ accessToken: 'token', displayName: 'Елена', email: 'prep@volga-auto.demo', organizationName: 'Волга Авто', branchName: 'Москва', permissions: ['reconditioning.view'] }))
afterEach(() => { clearSession(); vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('reconditioning workspace', () => {
  it('creates a draft with mandatory defects and renders the calculated budget', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString()
      if (url.endsWith('/api/reconditioning-plans/required-vehicles')) return Response.json([vehicle])
      if (url.endsWith('/api/reconditioning-plans/approvals')) return Response.json({ title: 'Недостаточно прав' }, { status: 403 })
      if (url.endsWith(`/api/vehicles/${vehicle.vehicleId}/reconditioning-plans`)) return Response.json(plan)
      if (url.endsWith(`/api/reconditioning-plans/${plan.id}`)) return Response.json(plan)
      return new Response(null, { status: 404 })
    }))
    renderWorkspace()

    await userEvent.click(await screen.findByRole('button', { name: 'Создать план' }))

    expect(await screen.findByRole('heading', { name: 'Состав работ' })).toBeInTheDocument()
    expect(screen.getByText('Обязательный дефект')).toBeInTheDocument()
    expect(screen.getAllByText(/32.*000,00/).length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: 'Отправить руководителю' })).toBeEnabled()
  })

  it('shows an actionable optimistic-concurrency conflict when submit loses a race', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString()
      if (url.endsWith('/api/reconditioning-plans/required-vehicles')) return Response.json([vehicle])
      if (url.endsWith('/api/reconditioning-plans/approvals')) return Response.json({ title: 'Недостаточно прав' }, { status: 403 })
      if (url.endsWith(`/api/vehicles/${vehicle.vehicleId}/reconditioning-plans`)) return Response.json(plan)
      if (url.endsWith(`/api/reconditioning-plans/${plan.id}/submit`) && init?.method === 'POST') return Response.json({ title: 'План изменён' }, { status: 409 })
      if (url.endsWith(`/api/reconditioning-plans/${plan.id}`)) return Response.json(plan)
      return new Response(null, { status: 404 })
    }))
    renderWorkspace()
    await userEvent.click(await screen.findByRole('button', { name: 'Создать план' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Отправить руководителю' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Конфликт версий')
  })

  it('lets a manager approve a submitted plan and switches it to immutable view', async () => {
    saveSession({ accessToken: 'token', displayName: 'Марина', email: 'manager@volga-auto.demo', organizationName: 'Волга Авто', branchName: 'Москва', permissions: ['reconditioning.view', 'reconditioning.approve'] })
    const submitted: ReconditioningPlan = { ...plan, status: 'Submitted', version: 2, submittedAt: '2026-07-13T10:20:00Z', history: [...plan.history, { id: '13111111-1111-4111-8111-111111111111', fromStatus: 'Draft', toStatus: 'Submitted', actorName: 'Елена Подготовка', occurredAt: '2026-07-13T10:20:00Z' }] }
    const approved: ReconditioningPlan = { ...submitted, status: 'Approved', version: 3, decidedAt: '2026-07-13T10:30:00Z', approvedBudgetSnapshots: [{ id: '14111111-1111-4111-8111-111111111111', laborAmount: 32000, partsAmount: 0, plannedTotalAmount: 32000, approvedLimitAmount: 30000, currency: 'RUB', approvedByName: 'Марина Руководитель', approvedAt: '2026-07-13T10:30:00Z' }] }
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString()
      if (url.endsWith('/api/reconditioning-plans/required-vehicles')) return Response.json([])
      if (url.endsWith('/api/reconditioning-plans/approvals')) return Response.json([{ ...submitted, works: undefined, history: undefined }])
      if (url.endsWith(`/api/reconditioning-plans/${plan.id}/approve`) && init?.method === 'POST') return Response.json(approved)
      if (url.endsWith(`/api/reconditioning-plans/${plan.id}`)) return Response.json(submitted)
      if (url.endsWith(`/api/vehicles/${vehicle.vehicleId}/reconditioning-plans`)) return Response.json([])
      return new Response(null, { status: 404 })
    }))
    renderWorkspace()
    await userEvent.click(await screen.findByRole('button', { name: /Lada XRAY/ }))
    await userEvent.clear(await screen.findByLabelText('Одобренный лимит'))
    await userEvent.type(screen.getByLabelText('Одобренный лимит'), '30000')
    await userEvent.click(screen.getByRole('button', { name: 'Утвердить бюджет' }))

    expect(await screen.findByText('Бюджет утверждён')).toBeInTheDocument()
    expect(screen.getByLabelText('Название работы: Повреждение бампера')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Сохранить работу' })).not.toBeInTheDocument()
  })
})
