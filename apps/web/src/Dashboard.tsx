import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import type { Vehicle } from './App'
import type { Lead } from './Crm'
import type { Deal } from './Deals'
import type { FinanceDashboard } from './Finance'
import type { Reservation } from './Reservations'
import type { Visit } from './Sales'
import { EmptyState, Icon, LoadingState, Money, PageHeader, VehicleVisual } from './ui'

type DashboardView = 'intake' | 'inspections' | 'reconditioning' | 'operations' | 'quality-listings' | 'crm' | 'sales' | 'reservations' | 'deals' | 'finance'

export function Dashboard({ vehicles, loading, permissions, onNavigate, onSelectVehicle }: {
  vehicles: Vehicle[]
  loading: boolean
  permissions: string[]
  onNavigate: (view: DashboardView) => void
  onSelectVehicle: (vehicle: Vehicle) => void
}) {
  const can = (permission: string) => permissions.includes(permission)
  const reservations = useQuery({ queryKey: ['reservations'], queryFn: () => api<Reservation[]>('/api/reservations'), enabled: can('reservations.view'), retry: false })
  const deals = useQuery({ queryKey: ['deals'], queryFn: () => api<Deal[]>('/api/deals'), enabled: can('deals.view'), retry: false })
  const leads = useQuery({ queryKey: ['crm-leads'], queryFn: () => api<Lead[]>('/api/crm/leads'), enabled: can('crm.leads.view'), retry: false })
  const visits = useQuery({ queryKey: ['sales-visits'], queryFn: () => api<Visit[]>('/api/sales/visits'), enabled: can('sales.visits.view'), retry: false })
  const financeRange = dashboardFinanceRange()
  const finance = useQuery({ queryKey: ['finance-dashboard', 'overview'], queryFn: () => api<FinanceDashboard>(`/api/finance/dashboard?${financeRange}`), enabled: can('finance.view'), retry: false })

  const attention = vehicles.filter((vehicle) => ['IntakeDraft', 'InspectionInProgress', 'ReconditioningRequired'].includes(vehicle.status))
  const activeReservations = (reservations.data ?? []).filter((item) => ['PendingDeposit', 'Active'].includes(item.status))
  const activeDeals = (deals.data ?? []).filter((item) => !['Completed', 'Cancelled', 'Refunded'].includes(item.status))
  const overdueLeads = (leads.data ?? []).filter((lead) => lead.slaBreached || Boolean(lead.nextActionDueAt && new Date(lead.nextActionDueAt) < new Date()))
  const upcomingVisits = (visits.data ?? []).filter((visit) => visit.status === 'Scheduled' && new Date(visit.startsAt) >= new Date()).sort((a, b) => a.startsAt.localeCompare(b.startsAt)).slice(0, 4)
  const sold = vehicles.filter((vehicle) => vehicle.status === 'Sold').length
  const stages = [
    { label: 'Поступление', value: vehicles.filter((x) => ['IntakeDraft', 'InStock'].includes(x.status)).length, view: 'intake' as const },
    { label: 'Осмотр', value: vehicles.filter((x) => x.status === 'InspectionInProgress').length, view: 'inspections' as const },
    { label: 'Подготовка', value: vehicles.filter((x) => x.status === 'ReconditioningRequired').length, view: 'reconditioning' as const },
    { label: 'Готовы к продаже', value: vehicles.filter((x) => ['InspectionPassed', 'ReadyForSale'].includes(x.status)).length, view: 'quality-listings' as const },
    { label: 'Продажи', value: vehicles.filter((x) => ['Reserved', 'SaleInProgress'].includes(x.status)).length, view: 'deals' as const },
    { label: 'Продано', value: sold, view: 'finance' as const },
  ]
  const anyQueryError = reservations.error || deals.error || leads.error || visits.error || finance.error

  return <>
    <PageHeader eyebrow="Рабочий день" title="Обзор" description="Главное по автомобилям, клиентам и экономике — из актуальных данных DealerOS.">
      {can('vehicles.create') && <button className="primary" onClick={() => onNavigate('intake')}><Icon name="intake" size={17} />Новое поступление</button>}
    </PageHeader>

    {anyQueryError && <div className="warning-banner dashboard-warning"><strong>Часть данных временно недоступна</strong><span>Основные рабочие разделы продолжают работать. Обновите страницу позже.</span></div>}

    <section className="dashboard-hero">
      <div className="dashboard-hero-copy"><span className="overline">Автопарк сегодня</span><strong>{loading ? '—' : vehicles.length}</strong><p>автомобилей в доступных филиалах</p></div>
      <div className="stage-overview">{stages.map((stage) => <button key={stage.label} onClick={() => onNavigate(stage.view)}><span>{stage.value}</span><small>{stage.label}</small><Icon name="chevron" size={15} /></button>)}</div>
    </section>

    <div className="dashboard-metrics">
      <DashboardMetric icon="alert" label="Требуют внимания" value={attention.length} hint="осмотр, решение или подготовка" tone={attention.length ? 'warning' : 'neutral'} onClick={() => onNavigate('intake')} />
      {can('crm.leads.view') && <DashboardMetric icon="customers" label="Просроченные обращения" value={overdueLeads.length} hint="нарушен SLA или срок действия" tone={overdueLeads.length ? 'danger' : 'neutral'} onClick={() => onNavigate('crm')} />}
      {can('reservations.view') && <DashboardMetric icon="reservation" label="Активные бронирования" value={activeReservations.length} hint="включая ожидание предоплаты" onClick={() => onNavigate('reservations')} />}
      {can('deals.view') && <DashboardMetric icon="deal" label="Сделки в работе" value={activeDeals.length} hint="до завершённой выдачи" onClick={() => onNavigate('deals')} />}
    </div>

    <div className="dashboard-grid">
      <section className="surface dashboard-attention">
        <div className="surface-heading"><div><span className="overline">Приоритет</span><h2>Автомобили, требующие действий</h2></div><button className="ghost-button" onClick={() => onNavigate('intake')}>Весь реестр <Icon name="chevron" size={15} /></button></div>
        {loading ? <LoadingState /> : attention.length ? <div className="attention-list">{attention.slice(0, 5).map((vehicle) => <button key={vehicle.id} onClick={() => { onSelectVehicle(vehicle); onNavigate('intake') }}><VehicleVisual name={`${vehicle.make} ${vehicle.model}`} /><span><strong>{vehicle.make} {vehicle.model}</strong><small>{vehicle.year} · {vehicle.branchName}</small><code>{vehicle.vin}</code></span><StatusText status={vehicle.status} /><Icon name="chevron" size={17} /></button>)}</div> : <EmptyState icon="quality" title="Срочных действий нет" description="Новые задачи появятся здесь по мере движения автомобилей." />}
      </section>

      <section className="surface dashboard-agenda">
        <div className="surface-heading"><div><span className="overline">Расписание</span><h2>Ближайшие визиты</h2></div>{can('sales.visits.view') && <button className="ghost-button" onClick={() => onNavigate('sales')}>Календарь <Icon name="chevron" size={15} /></button>}</div>
        {!can('sales.visits.view') ? <EmptyState icon="calendar" title="Раздел недоступен" description="Визиты скрыты согласно вашим правам." /> : visits.isLoading ? <LoadingState /> : upcomingVisits.length ? <div className="agenda-list">{upcomingVisits.map((visit) => <article key={visit.id}><time><strong>{new Date(visit.startsAt).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })}</strong><small>{new Date(visit.startsAt).toLocaleDateString('ru-RU', { day: 'numeric', month: 'short' })}</small></time><span><strong>{visit.customerName}</strong><small>{visit.vehicleName} · {visit.responsibleName}</small></span></article>)}</div> : <EmptyState icon="calendar" title="Визитов не запланировано" description="Новые встречи появятся после назначения в разделе продаж." />}
      </section>
    </div>

    {can('finance.view') && <section className="surface dashboard-finance">
      <div className="surface-heading"><div><span className="overline">Последние 90 дней</span><h2>Фактическая экономика</h2></div><button className="ghost-button" onClick={() => onNavigate('finance')}>Подробнее <Icon name="chevron" size={15} /></button></div>
      {finance.isLoading ? <LoadingState /> : finance.data?.groups.length ? <div className="finance-overview">{finance.data.groups.map((group) => <article key={group.currency}><span>{group.currency}</span><div><small>Чистая выручка</small><Money value={group.netRevenue} currency={group.currency} /></div><div><small>Фактическая прибыль</small><Money value={group.actualProfit} currency={group.currency} className={group.actualProfit < 0 ? 'negative-value' : ''} /></div><div><small>Продано</small><strong>{group.soldVehicles}</strong></div><div><small>Средняя маржа</small><strong>{group.averageMarginPercent == null ? '—' : `${group.averageMarginPercent}%`}</strong></div></article>)}</div> : <EmptyState icon="finance" title="Пока нет завершённых сделок" description="Экономика появится после выдачи автомобиля и фиксации расчёта прибыли." />}
    </section>}
  </>
}

function DashboardMetric({ icon, label, value, hint, tone = 'neutral', onClick }: { icon: 'alert' | 'customers' | 'reservation' | 'deal'; label: string; value: number; hint: string; tone?: 'neutral' | 'warning' | 'danger'; onClick: () => void }) {
  return <button className={`dashboard-metric ${tone}`} onClick={onClick}><span className="metric-icon"><Icon name={icon} size={20} /></span><span><small>{label}</small><strong>{value}</strong><em>{hint}</em></span><Icon name="chevron" size={16} /></button>
}

function StatusText({ status }: { status: Vehicle['status'] }) {
  const labels: Record<Vehicle['status'], string> = { IntakeDraft: 'Завершить приёмку', InStock: 'Назначить осмотр', InspectionInProgress: 'Завершить осмотр', ReconditioningRequired: 'Создать план', InspectionPassed: 'Проверить качество', ReadyForSale: 'Готов к продаже', Reserved: 'Активная бронь', SaleInProgress: 'Сделка в работе', Sold: 'Продан' }
  return <span className={`attention-status ${status === 'ReconditioningRequired' ? 'warning' : ''}`}>{labels[status]}</span>
}

function dashboardFinanceRange() {
  const to = new Date(); to.setUTCDate(to.getUTCDate() + 1)
  const from = new Date(to.getTime() - 90 * 86_400_000)
  return `from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}`
}
