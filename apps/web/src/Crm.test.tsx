import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { CrmWorkspace, type Customer, type Lead } from './Crm'

const branch = { id: '10000000-0000-4000-8000-000000000001', code: 'MSK', name: 'Москва' }
const customer: Customer = { id: '20000000-0000-4000-8000-000000000001', createdInBranchId: branch.id, type: 'Individual', name: 'Иван Петров', normalizedPhone: '+79991234567', preferredChannel: 'Phone', consentGiven: true, marketingConsent: false, isMerged: false, version: 1, createdAt: '2026-07-14T10:00:00Z' }
const lead: Lead = { id: '30000000-0000-4000-8000-000000000001', branchId: branch.id, customerId: customer.id, customerName: customer.name, searchCriteria: 'Кроссовер до 2 млн ₽', source: 'Входящий звонок', status: 'New', createdAt: '2026-07-14T10:00:00Z', firstResponseDueAt: '2026-07-14T10:30:00Z', slaBreached: true, version: 1, activities: [], history: [] }

function setup(permissions: string[]) {
  sessionStorage.setItem('dealeros.session', JSON.stringify({ accessToken: 'token', permissions }))
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><CrmWorkspace /></QueryClientProvider>)
}

describe('CrmWorkspace', () => {
  beforeEach(() => { sessionStorage.clear(); vi.restoreAllMocks() })

  it('warns about duplicate contacts and requires an explicit merge action', async () => {
    const duplicate = { ...customer, id: '20000000-0000-4000-8000-000000000002', name: 'И. Петров' }
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.includes('/api/crm/customers?')) return Response.json([])
      if (url.endsWith('/api/crm/leads')) return Response.json([])
      if (url.endsWith('/api/branches')) return Response.json([branch])
      if (url.endsWith('/api/vehicles')) return Response.json([])
      if (url.endsWith('/api/crm/customers') && init?.method === 'POST') return Response.json({ customer, possibleDuplicates: [duplicate] })
      if (url.endsWith(`/api/crm/customers/${customer.id}/merge-preview/${duplicate.id}`)) return Response.json({ source: customer, target: duplicate, leadsToMove: 2, warning: 'Источник будет сохранён как merged' })
      throw new Error(`Unexpected ${url}`)
    })
    setup(['crm.customers.view', 'crm.customers.edit', 'crm.customers.merge', 'crm.leads.view'])
    await userEvent.type(await screen.findByLabelText('Имя клиента'), customer.name)
    await userEvent.type(screen.getByLabelText('Телефон клиента'), '8 999 123-45-67')
    await userEvent.click(screen.getByRole('button', { name: 'Создать клиента' }))
    expect(await screen.findByText('Возможный дубль')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: `Предпросмотр: ${duplicate.name}` }))
    expect(await screen.findByText('Будет перемещено обращений: 2')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: `Подтвердить объединение с ${duplicate.name}` })).toBeEnabled()
  })

  it('shows an overdue lead and records the first meaningful contact through explicit commands', async () => {
    let current = lead
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.includes('/api/crm/customers?')) return Response.json([customer])
      if (url.endsWith('/api/crm/leads') && (init?.method ?? 'GET') === 'GET') return Response.json([current])
      if (url.endsWith('/api/branches')) return Response.json([branch])
      if (url.endsWith('/api/vehicles')) return Response.json([])
      if (url.includes('/api/crm/managers?')) return Response.json([{ id: '40000000-0000-4000-8000-000000000001', displayName: 'Анна Руководитель' }])
      if (url.endsWith(`/api/crm/leads/${lead.id}/assign-round-robin`) && init?.method === 'POST') {
        current = { ...current, status: 'Assigned', assignedManagerName: 'Анна Руководитель', version: 2, slaBreached: true }
        return Response.json(current)
      }
      if (url.endsWith(`/api/crm/leads/${lead.id}/activities`) && init?.method === 'POST') {
        current = { ...current, status: 'FirstContact', firstResponseAt: '2026-07-14T10:40:00Z', slaBreached: false, version: 4, activities: [{ id: '50000000-0000-4000-8000-000000000001', type: 'Call', direction: 'Outbound', result: 'Answered', summary: 'Клиент подтвердил интерес', isClosed: true, createdAt: '2026-07-14T10:40:00Z' }] }
        return Response.json(current)
      }
      throw new Error(`Unexpected ${url}`)
    })
    setup(['crm.customers.view', 'crm.leads.view', 'crm.leads.assign', 'crm.leads.work'])
    const leadButton = (await screen.findByText('Кроссовер до 2 млн ₽ · Входящий звонок')).closest('button')!
    expect(leadButton).toHaveTextContent('SLA просрочен')
    await userEvent.click(leadButton)
    await userEvent.click(screen.getByRole('button', { name: 'Назначить автоматически' }))
    expect(await screen.findByText('Анна Руководитель')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Итог первого контакта'), 'Клиент подтвердил интерес')
    await userEvent.click(screen.getByRole('button', { name: 'Зафиксировать первый контакт' }))
    expect(await screen.findByText('Звонок · Исходящий')).toBeInTheDocument()
    expect(screen.getByText('Первый контакт', { selector: 'span' })).toBeInTheDocument()
  })
})
