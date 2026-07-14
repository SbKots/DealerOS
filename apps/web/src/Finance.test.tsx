import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { FinanceWorkspace, type FinanceDashboard } from './Finance'

const vehicleId = '10000000-0000-4000-8000-000000000001'
const dashboard: FinanceDashboard = { from: '2026-01-01T00:00:00Z', to: '2026-12-31T00:00:00Z', groups: [{
  currency: 'RUB', soldVehicles: 1, grossRevenue: 1_500_000, netRevenue: 1_450_000,
  totalCost: 1_120_000, actualProfit: 330_000, averageMarginPercent: 22.76,
  planFactProfitVariance: -70_000, averageDaysInStock: 18, lossVehicles: [], vehicles: [{
    vehicleId, vehicleName: 'Volkswagen Passat', vin: 'WVWZZZ1JZXW654321',
    dealId: '20000000-0000-4000-8000-000000000001', branchId: '30000000-0000-4000-8000-000000000001',
    currency: 'RUB', grossRevenue: 1_500_000, netRevenue: 1_450_000, totalCost: 1_120_000,
    actualProfit: 330_000, actualMarginPercent: 22.76, planProfit: 400_000,
    profitVariance: -70_000, daysInStock: 18, soldAt: '2026-07-14T09:00:00Z',
  }],
}] }

function setup(permissions = ['finance.view', 'finance.export']) {
  sessionStorage.setItem('dealeros.session', JSON.stringify({ accessToken: 'token', permissions }))
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><FinanceWorkspace /></QueryClientProvider>)
}

describe('FinanceWorkspace', () => {
  beforeEach(() => { sessionStorage.clear(); vi.restoreAllMocks() })

  it('shows currency-separated plan/fact totals and vehicle rows', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(Response.json(dashboard))
    setup()
    expect(await screen.findByText('Volkswagen Passat')).toBeInTheDocument()
    expect(screen.getByText('Итоги без смешивания валют')).toBeInTheDocument()
    expect(screen.getAllByText('22.76%')).toHaveLength(2)
    expect(screen.getByText(/-70/)).toBeInTheDocument()
  })

  it('drills into immutable source totals and revisions', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input)
      if (url.includes('/api/finance/dashboard')) return Response.json(dashboard)
      if (url.includes(`/api/finance/vehicles/${vehicleId}`)) return Response.json({
        vehicleId, vehicleName: 'Volkswagen Passat', vin: 'WVWZZZ1JZXW654321',
        dealId: dashboard.groups[0].vehicles[0].dealId, soldAt: '2026-07-14T09:00:00Z',
        latest: { id: '40000000-0000-4000-8000-000000000001', revision: 2, reason: 'Manual cost corrected',
          formulaVersion: 'DealerOS.Profit.v1', currency: 'RUB', grossRevenue: 1_500_000, refunds: 50_000,
          netRevenue: 1_450_000, purchaseCost: 1_100_000, operationsCost: 0, manualCost: 20_000,
          totalCost: 1_120_000, actualProfit: 330_000, actualMarginPercent: 22.76, planProfit: 400_000,
          planMarginPercent: 26.67, sourcesJson: '{}', sha256: 'a'.repeat(64), createdAt: '2026-07-14T09:00:00Z' },
        revisions: [{ id: '40000000-0000-4000-8000-000000000001', revision: 2,
          reason: 'Manual cost corrected', formulaVersion: 'DealerOS.Profit.v1', currency: 'RUB',
          grossRevenue: 1_500_000, refunds: 50_000, netRevenue: 1_450_000, purchaseCost: 1_100_000,
          operationsCost: 0, manualCost: 20_000, totalCost: 1_120_000, actualProfit: 330_000,
          actualMarginPercent: 22.76, planProfit: 400_000, planMarginPercent: 26.67, sourcesJson: '{}',
          sha256: 'a'.repeat(64), createdAt: '2026-07-14T09:00:00Z' }], manualCosts: [],
      })
      throw new Error(`Unexpected ${url}`)
    })
    setup()
    await userEvent.click(await screen.findByRole('button', { name: 'Источники →' }))
    expect(await screen.findByText('Продажа (Deal)')).toBeInTheDocument()
    expect(screen.getByText('Подготовка (Operations)')).toBeInTheDocument()
    expect(screen.getByText(/Revision 2/)).toBeInTheDocument()
    expect(screen.getByText(/SHA-256 aaaaaaaaaaaa/)).toBeInTheDocument()
  })

  it('renders an explicit negative-profit state and hides export without permission', async () => {
    const loss = structuredClone(dashboard); loss.groups[0].actualProfit = -10_000
    loss.groups[0].vehicles[0].actualProfit = -10_000; loss.groups[0].lossVehicles = loss.groups[0].vehicles
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(Response.json(loss))
    setup(['finance.view'])
    expect(await screen.findByText('Убыточные автомобили')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Экспорт CSV' })).not.toBeInTheDocument()
    expect(screen.getAllByText(/-10/)[0]).toHaveClass('negative-value')
  })
})
