import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, apiBlob, ApiError, getSession } from './api'

export type FinanceVehicle = { vehicleId: string; vehicleName: string; vin: string; dealId: string; branchId: string; currency: string; grossRevenue: number; netRevenue: number; totalCost: number; actualProfit: number; actualMarginPercent?: number; planProfit: number; profitVariance: number; daysInStock?: number; soldAt: string }
export type FinanceGroup = { currency: string; soldVehicles: number; grossRevenue: number; netRevenue: number; totalCost: number; actualProfit: number; averageMarginPercent?: number; planFactProfitVariance: number; averageDaysInStock?: number; vehicles: FinanceVehicle[]; lossVehicles: FinanceVehicle[] }
export type FinanceDashboard = { from: string; to: string; branchId?: string; groups: FinanceGroup[] }
type Snapshot = { id: string; revision: number; reason: string; formulaVersion: string; currency: string; grossRevenue: number; refunds: number; netRevenue: number; purchaseCost: number; operationsCost: number; manualCost: number; totalCost: number; actualProfit: number; actualMarginPercent?: number; planProfit: number; planMarginPercent?: number; sourcesJson: string; sha256: string; createdAt: string }
type Economics = { vehicleId: string; vehicleName: string; vin: string; dealId: string; soldAt: string; latest: Snapshot; revisions: Snapshot[]; manualCosts: { id: string; category: string; source: string; reference: string; amount: number; currency: string; superseded: boolean; correctionReason?: string }[] }

export function FinanceWorkspace() {
  const permissions = getSession()?.permissions ?? []
  const today = new Date().toISOString().slice(0, 10)
  const [from, setFrom] = useState(new Date(Date.now() - 90 * 86_400_000).toISOString().slice(0, 10))
  const [to, setTo] = useState(today)
  const [currency, setCurrency] = useState('')
  const [selectedVehicle, setSelectedVehicle] = useState('')
  const endExclusive = new Date(`${to || today}T00:00:00Z`); endExclusive.setUTCDate(endExclusive.getUTCDate() + 1)
  const suffix = `from=${encodeURIComponent(`${from || today}T00:00:00Z`)}&to=${encodeURIComponent(endExclusive.toISOString())}${currency ? `&currency=${currency}` : ''}`
  const query = useQuery({ queryKey: ['finance-dashboard', from, to, currency], queryFn: () => api<FinanceDashboard>(`/api/finance/dashboard?${suffix}`) })
  const detail = useQuery({ queryKey: ['finance-vehicle', selectedVehicle], queryFn: () => api<Economics>(`/api/finance/vehicles/${selectedVehicle}`), enabled: Boolean(selectedVehicle) })
  const groups = query.data?.groups ?? []

  const exportCsv = async () => {
    const blob = await apiBlob(`/api/finance/export.csv?${suffix}`)
    const url = URL.createObjectURL(blob); const anchor = document.createElement('a')
    anchor.href = url; anchor.download = `dealeros-profit-${from}-${to}.csv`; anchor.click(); URL.revokeObjectURL(url)
  }

  return <>
    <div className="page-heading"><div><p className="eyebrow">Аналитика</p><h1>Экономика проданных автомобилей</h1><p className="muted">План и факт связаны с продажей, возвратами, закупкой и расходами на подготовку. Валюты рассчитываются раздельно.</p></div><div className="metric"><span>Продано</span><strong>{groups.reduce((sum, x) => sum + x.soldVehicles, 0)}</strong><small>в выбранном периоде</small></div></div>
    <section className="panel finance-filters"><label>С даты<input aria-label="Финансы с даты" type="date" value={from} onChange={(event) => setFrom(event.target.value)} /></label><label>По дату<input aria-label="Финансы по дату" type="date" value={to} onChange={(event) => setTo(event.target.value)} /></label><label>Валюта<select aria-label="Валюта отчёта" value={currency} onChange={(event) => setCurrency(event.target.value)}><option value="">Все, раздельно</option><option value="RUB">RUB</option><option value="USD">USD</option><option value="EUR">EUR</option></select></label>{permissions.includes('finance.export') && <button className="secondary" onClick={exportCsv}>Экспорт CSV</button>}</section>
    {(query.error || detail.error) && <div className="error-banner workspace-error" role="alert">{errorText(query.error ?? detail.error)}</div>}
    {query.isLoading ? <section className="panel"><p>Загрузка экономики…</p></section> : groups.length === 0 ? <section className="panel empty-state"><strong>Нет завершённых сделок</strong><p>После выдачи автомобиля здесь появится зафиксированный расчёт прибыли.</p></section> : groups.map((group) => <section className="panel finance-group" key={group.currency}>
      <div className="panel-title"><span className="step">{group.currency}</span><div><h2>Итоги без смешивания валют</h2><p>{group.soldVehicles} автомобилей · средний срок {group.averageDaysInStock ?? '—'} дн.</p></div></div>
      <div className="economics finance-metrics"><Metric label="Валовая выручка" value={group.grossRevenue} currency={group.currency} /><Metric label="Чистая выручка" value={group.netRevenue} currency={group.currency} /><Metric label="Общие расходы" value={group.totalCost} currency={group.currency} /><Metric label="Фактическая прибыль" value={group.actualProfit} currency={group.currency} danger={group.actualProfit < 0} /><div><span>Средняя маржа</span><strong>{group.averageMarginPercent == null ? '—' : `${group.averageMarginPercent}%`}</strong></div><Metric label="План / факт" value={group.planFactProfitVariance} currency={group.currency} danger={group.planFactProfitVariance < 0} /></div>
      {group.lossVehicles.length > 0 && <div className="warning-banner"><strong>Убыточные автомобили</strong><span>{group.lossVehicles.map((x) => x.vehicleName).join(', ')}</span></div>}
      <div className="table-wrap"><table><thead><tr><th>Автомобиль</th><th>Продан</th><th>План</th><th>Факт</th><th>Маржа</th><th /></tr></thead><tbody>{group.vehicles.map((vehicle) => <tr key={vehicle.vehicleId}><td><strong>{vehicle.vehicleName}</strong><small className="mono">{vehicle.vin}</small></td><td>{new Date(vehicle.soldAt).toLocaleDateString('ru-RU')}</td><td>{money(vehicle.planProfit, vehicle.currency)}</td><td className={vehicle.actualProfit < 0 ? 'negative-value' : ''}>{money(vehicle.actualProfit, vehicle.currency)}</td><td>{vehicle.actualMarginPercent == null ? '—' : `${vehicle.actualMarginPercent}%`}</td><td><button className="link-button" onClick={() => setSelectedVehicle(vehicle.vehicleId)}>Источники →</button></td></tr>)}</tbody></table></div>
    </section>)}
    {selectedVehicle && <section className="panel finance-detail">{detail.isLoading ? <p>Загрузка источников…</p> : detail.data && <><button className="link-button" onClick={() => setSelectedVehicle('')}>← Закрыть детализацию</button><div className="work-card-head"><div><p className="eyebrow">Завершённый цикл · только чтение</p><h2>{detail.data.vehicleName}</h2><p className="mono">{detail.data.vin}</p></div><strong className={detail.data.latest.actualProfit < 0 ? 'negative-value' : ''}>{money(detail.data.latest.actualProfit, detail.data.latest.currency)}</strong></div><div className="source-grid"><Source label="Продажа" value={detail.data.latest.grossRevenue} snapshot={detail.data.latest} /><Source label="Возвраты" value={detail.data.latest.refunds} snapshot={detail.data.latest} /><Source label="Закупка" value={detail.data.latest.purchaseCost} snapshot={detail.data.latest} /><Source label="Подготовка" value={detail.data.latest.operationsCost} snapshot={detail.data.latest} /><Source label="Прочие расходы" value={detail.data.latest.manualCost} snapshot={detail.data.latest} /></div><h3>Ревизии расчёта</h3><div className="payment-timeline">{[...detail.data.revisions].reverse().map((snapshot) => <article key={snapshot.id}><span>{new Date(snapshot.createdAt).toLocaleString('ru-RU')}</span><strong>Ревизия {snapshot.revision} · {snapshot.reason}</strong><b>{money(snapshot.actualProfit, snapshot.currency)}</b><small>{snapshot.formulaVersion} · SHA-256 {snapshot.sha256.slice(0, 12)}…</small></article>)}</div></>}</section>}
  </>
}

function Metric({ label, value, currency, danger = false }: { label: string; value: number; currency: string; danger?: boolean }) { return <div><span>{label}</span><strong className={danger ? 'negative-value' : ''}>{money(value, currency)}</strong></div> }
function Source({ label, value, snapshot }: { label: string; value: number; snapshot: Snapshot }) { return <article><span>{label}</span><strong>{money(value, snapshot.currency)}</strong><small>Источник зафиксирован в снимке расчёта, рев. {snapshot.revision}</small></article> }
function money(value: number, currency: string) { return new Intl.NumberFormat('ru-RU', { style: 'currency', currency }).format(value) }
function errorText(error: unknown) { return error instanceof ApiError ? error.message : 'Не удалось загрузить финансовый отчёт.' }
