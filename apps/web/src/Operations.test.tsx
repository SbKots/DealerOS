import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { OperationsWorkspace, type ReconditioningExecution } from './Operations'

const execution: ReconditioningExecution = {
  id: '10000000-0000-4000-8000-000000000001', vehicleId: '20000000-0000-4000-8000-000000000001', planId: '30000000-0000-4000-8000-000000000001', status: 'Draft', plannedAmount: 10000, approvedLimitAmount: 12000,
  actualLaborAmount: 0, actualMaterialAmount: 0, actualExternalAmount: 0, actualTotalAmount: 0, varianceAmount: -12000, currency: 'RUB', version: 1, notifications: [],
  workOrders: [{ id: '40000000-0000-4000-8000-000000000001', title: 'Заменить колодки', isMandatory: true, executorType: 'Internal', assigneeName: 'Цех', dueAt: '2026-07-20T10:00:00Z', status: 'Scheduled', plannedLaborAmount: 6000, plannedPartsAmount: 4000, actualLaborHours: 0, actualLaborAmount: 0, actualMaterialAmount: 0, actualExternalAmount: 0, currency: 'RUB', settlementStatus: 'NotRequired', materials: [] }],
}

function setup() { return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><OperationsWorkspace /></QueryClientProvider>) }

describe('OperationsWorkspace', () => {
  beforeEach(() => { sessionStorage.clear(); vi.restoreAllMocks() })

  it('creates an execution from an approved plan and shows its immutable budget baseline', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/reconditioning-plans/required-vehicles')) return Response.json([{ vehicleId: execution.vehicleId, inspectionId: '50000000-0000-4000-8000-000000000001', branchName: 'Центр', vin: 'XTA210990Y2765432', make: 'Lada', model: 'Vesta', inspectionCompletedAt: '2026-07-13T10:00:00Z', mandatoryDefectCount: 1, latestPlanId: execution.planId, latestPlanStatus: 'Approved', latestPlanRevision: 1 }])
      if (url.endsWith('/api/operations/executions') && init?.method === 'POST') return Response.json(execution)
      throw new Error(`Unexpected ${url}`)
    })
    setup()
    await userEvent.click(await screen.findByRole('button', { name: 'Открыть выполнение' }))
    expect(await screen.findByRole('heading', { name: 'Фактическое выполнение работ' })).toBeInTheDocument()
    expect(screen.getByText('12 000,00 ₽')).toBeInTheDocument()
    expect(screen.getByText('Заменить колодки')).toBeInTheDocument()
  })

  it('sends an explicit start command with optimistic version', async () => {
    const started = { ...execution, status: 'InProgress', version: 2 }
    const fetch = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/reconditioning-plans/required-vehicles')) return Response.json([{ vehicleId: execution.vehicleId, branchName: 'Центр', vin: 'XTA210990Y2765432', make: 'Lada', model: 'Vesta', inspectionCompletedAt: '2026-07-13T10:00:00Z', mandatoryDefectCount: 1, latestPlanId: execution.planId, latestPlanStatus: 'Approved', latestPlanRevision: 1 }])
      if (url.endsWith('/api/operations/executions')) return Response.json(execution)
      if (url.endsWith(`/api/operations/executions/${execution.id}/start`) && init?.method === 'POST') return Response.json(started)
      throw new Error(`Unexpected ${url}`)
    })
    setup()
    await userEvent.click(await screen.findByRole('button', { name: 'Открыть выполнение' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Начать выполнение' }))
    await waitFor(() => expect(screen.getByText('Execution · InProgress')).toBeInTheDocument())
    expect(fetch).toHaveBeenLastCalledWith(expect.stringContaining('/start'), expect.objectContaining({ body: JSON.stringify({ expectedVersion: 1 }) }))
  })

  it('locks work inputs while a server command is pending', async () => {
    const started = { ...execution, status: 'InProgress', version: 2 }
    let resolveStart!: (response: Response) => void
    const startResponse = new Promise<Response>((resolve) => { resolveStart = resolve })
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.endsWith('/api/reconditioning-plans/required-vehicles')) return Response.json([{ vehicleId: execution.vehicleId, branchName: 'Центр', vin: 'XTA210990Y2765432', make: 'Lada', model: 'Vesta', inspectionCompletedAt: '2026-07-13T10:00:00Z', mandatoryDefectCount: 1, latestPlanId: execution.planId, latestPlanStatus: 'Approved', latestPlanRevision: 1 }])
      if (url.endsWith('/api/operations/executions')) return Response.json(execution)
      if (url.endsWith(`/api/operations/executions/${execution.id}/start`) && init?.method === 'POST') return startResponse
      throw new Error(`Unexpected ${url}`)
    })
    setup()
    await userEvent.click(await screen.findByRole('button', { name: 'Открыть выполнение' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Начать выполнение' }))
    const actual = screen.getByRole('spinbutton', { name: `Факт работы: ${execution.workOrders[0].title}` })
    await waitFor(() => expect(actual).toBeDisabled())

    resolveStart(Response.json(started))

    await waitFor(() => expect(screen.getByRole('spinbutton', { name: `Факт работы: ${execution.workOrders[0].title}` })).toBeEnabled())
  })
})
