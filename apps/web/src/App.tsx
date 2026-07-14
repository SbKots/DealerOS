import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { api, ApiError, clearSession, getSession, hasSession, saveSession, sessionExpiredEvent, type Session } from './api'
import { CrmWorkspace } from './Crm'
import { Dashboard } from './Dashboard'
import { DealsWorkspace } from './Deals'
import { FinanceWorkspace } from './Finance'
import { InspectionsWorkspace } from './Inspections'
import { OperationsWorkspace } from './Operations'
import { QualityListingsWorkspace } from './QualityListings'
import { ReconditioningWorkspace } from './Reconditioning'
import { ReservationsWorkspace } from './Reservations'
import { SalesWorkspace } from './Sales'
import { VehicleCover, VehiclePhotoGallery } from './VehiclePhotoGallery'
import { AppLogo, EmptyState, Icon, type IconName, PageHeader, VehicleJourney } from './ui'
import './App.css'

const intakeSchema = z.object({
  branchId: z.string().uuid('Выберите филиал'),
  vin: z.string().trim().toUpperCase().regex(/^[A-HJ-NPR-Z0-9]{17}$/, 'VIN: 17 символов без I, O и Q'),
  make: z.string().trim().min(1, 'Укажите марку').max(100, 'Не более 100 символов'),
  model: z.string().trim().min(1, 'Укажите модель').max(100, 'Не более 100 символов'),
  year: z.coerce.number().int().min(1950).max(new Date().getFullYear() + 1),
  mileageKm: z.coerce.number().int().min(0).max(3_000_000),
  plannedPurchaseAmount: z.coerce.number().positive('Цена должна быть больше нуля'),
  currency: z.literal('RUB'),
})

type IntakeInput = z.input<typeof intakeSchema>
type IntakeForm = z.output<typeof intakeSchema>
export type Branch = { id: string; code: string; name: string }
export type Vehicle = {
  id: string; branchId: string; branchName: string; vin: string; make: string; model: string; year: number
  mileageKm: number; plannedPurchaseAmount: number; currency: string; status: 'IntakeDraft' | 'InStock' | 'InspectionInProgress' | 'ReconditioningRequired' | 'InspectionPassed' | 'ReadyForSale' | 'Reserved' | 'SaleInProgress' | 'Sold'
  stockNumber?: string; coverPhotoId?: string; createdAt: string; acceptedAt?: string; version: number
}

type View = 'dashboard' | 'intake' | 'inspections' | 'reconditioning' | 'operations' | 'quality-listings' | 'crm' | 'sales' | 'reservations' | 'deals' | 'finance'
type NavItem = { view: View; label: string; shortLabel?: string; icon: IconName; permission?: string | string[] }
type NavGroup = { label: string; items: NavItem[] }

const navigation: NavGroup[] = [
  { label: 'Главное', items: [{ view: 'dashboard', label: 'Обзор', icon: 'dashboard' }] },
  { label: 'Автомобили', items: [
    { view: 'intake', label: 'Приёмка', icon: 'intake', permission: 'vehicles.read' },
    { view: 'inspections', label: 'Осмотры', icon: 'inspection', permission: 'vehicles.inspections.view' },
    { view: 'reconditioning', label: 'Подготовка', icon: 'tools', permission: 'reconditioning.view' },
    { view: 'operations', label: 'Выполнение', icon: 'operations', permission: 'operations.view' },
    { view: 'quality-listings', label: 'Качество и контент', shortLabel: 'Качество', icon: 'quality', permission: ['quality.view', 'listings.view'] },
  ] },
  { label: 'Продажи', items: [
    { view: 'crm', label: 'Клиенты и обращения', shortLabel: 'Клиенты', icon: 'customers', permission: ['crm.customers.view', 'crm.leads.view'] },
    { view: 'sales', label: 'Визиты и предложения', shortLabel: 'Визиты', icon: 'sales', permission: ['sales.visits.view', 'sales.offers.view'] },
    { view: 'reservations', label: 'Бронирования', shortLabel: 'Брони', icon: 'reservation', permission: 'reservations.view' },
    { view: 'deals', label: 'Сделки', icon: 'deal', permission: 'deals.view' },
  ] },
  { label: 'Аналитика', items: [{ view: 'finance', label: 'Экономика', icon: 'finance', permission: 'finance.view' }] },
]

const viewTitles: Record<View, string> = {
  dashboard: 'Обзор', intake: 'Приёмка и автомобили', inspections: 'Осмотры и дефекты', reconditioning: 'План подготовки', operations: 'Выполнение работ',
  'quality-listings': 'Качество и контент', crm: 'Клиенты и обращения', sales: 'Визиты и предложения', reservations: 'Бронирования', deals: 'Сделки', finance: 'Экономика',
}

function errorText(error: unknown) {
  return error instanceof ApiError ? error.message : 'Не удалось выполнить операцию. Повторите попытку.'
}

export default function App() {
  const queryClient = useQueryClient()
  const [authenticated, setAuthenticated] = useState(hasSession())
  const [selected, setSelected] = useState<Vehicle | null>(null)
  const [view, setView] = useState<View>('dashboard')
  const [inspectionVehicle, setInspectionVehicle] = useState<Vehicle | null>(null)
  const [email, setEmail] = useState('admin@volga-auto.demo')
  const [password, setPassword] = useState('DealerOS!2026')
  const [sidebarCompact, setSidebarCompact] = useState(false)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)

  const branches = useQuery({ queryKey: ['branches'], queryFn: () => api<Branch[]>('/api/branches'), enabled: authenticated })
  const vehicles = useQuery({ queryKey: ['vehicles'], queryFn: () => api<Vehicle[]>('/api/vehicles'), enabled: authenticated })

  const form = useForm<IntakeInput, unknown, IntakeForm>({
    resolver: zodResolver(intakeSchema),
    defaultValues: { vin: '', make: '', model: '', year: 2022, mileageKm: 0, plannedPurchaseAmount: 0, currency: 'RUB' },
  })

  useEffect(() => {
    if (branches.data?.length && !form.getValues('branchId')) form.setValue('branchId', branches.data[0].id, { shouldValidate: true })
  }, [branches.data, form])

  useEffect(() => {
    if (!selected?.id) return
    const refreshed = vehicles.data?.find((vehicle) => vehicle.id === selected.id)
    if (refreshed && refreshed !== selected) setSelected(refreshed)
  }, [vehicles.data, selected])

  useEffect(() => {
    const handleExpiredSession = () => {
      setAuthenticated(false); setSelected(null); setView('dashboard'); queryClient.clear()
    }
    window.addEventListener(sessionExpiredEvent, handleExpiredSession)
    return () => window.removeEventListener(sessionExpiredEvent, handleExpiredSession)
  }, [queryClient])

  useEffect(() => {
    if (!mobileMenuOpen) return
    const close = (event: KeyboardEvent) => { if (event.key === 'Escape') setMobileMenuOpen(false) }
    window.addEventListener('keydown', close)
    return () => window.removeEventListener('keydown', close)
  }, [mobileMenuOpen])

  const login = useMutation({
    mutationFn: () => api<Session>('/api/auth/login', { method: 'POST', body: { email, password }, authenticated: false }),
    onSuccess: (session) => { saveSession(session); setView('dashboard'); setAuthenticated(true) },
  })

  const createVehicle = useMutation({
    mutationFn: (values: IntakeForm) => api<Vehicle>('/api/vehicles', { method: 'POST', body: values }),
    onSuccess: (vehicle) => {
      setSelected(vehicle)
      queryClient.invalidateQueries({ queryKey: ['vehicles'] })
      form.reset({ ...form.getValues(), vin: '', make: '', model: '', mileageKm: 0, plannedPurchaseAmount: 0 })
    },
  })

  const acceptVehicle = useMutation({
    mutationFn: (id: string) => api<Vehicle>(`/api/vehicles/${id}/accept-to-stock`, { method: 'POST' }),
    onSuccess: (vehicle) => { setSelected(vehicle); queryClient.invalidateQueries({ queryKey: ['vehicles'] }) },
  })

  if (!authenticated) {
    return <main className="login-shell">
      <section className="login-story">
        <AppLogo inverse />
        <div className="login-story-copy"><p className="eyebrow">DealerOS 1.0</p><h1>Весь путь автомобиля.<br />В одном рабочем пространстве.</h1><p>От приёмки и осмотра до сделки, выдачи и фактической экономики — с прозрачной историей каждого решения.</p></div>
        <div className="login-route" aria-label="Путь автомобиля"><span>Поступление</span><i /><span>Подготовка</span><i /><span>Продажа</span><i /><span>Экономика</span></div>
        <svg className="login-car" viewBox="0 0 720 250" aria-hidden="true"><path d="M82 174c31-9 55-59 95-95 25-23 60-31 126-35 101-6 178 10 231 43 25 16 48 48 70 78l56 13c18 4 29 17 29 39v12H35v-20c0-16 10-28 26-32z" /><path d="m190 85 109-13c75-8 148 1 197 27 18 10 34 27 51 51H145z" /><circle cx="173" cy="207" r="49" /><circle cx="554" cy="207" r="49" /></svg>
      </section>
      <section className="login-panel-wrap"><form className="login-panel" onSubmit={(event) => { event.preventDefault(); login.mutate() }}>
        <div className="login-mobile-logo"><AppLogo /></div>
        <span className="demo-badge">Локальный демонстрационный MVP</span>
        <div><p className="eyebrow">Добро пожаловать</p><h2 aria-label="Войдите в рабочее пространство">Войдите в DealerOS</h2><p className="muted">Демо-аккаунт уже заполнен. Используйте его для знакомства с полным сценарием.</p></div>
        <div className="login-fields"><label>Email<input aria-label="Email" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} /></label><label>Пароль<input aria-label="Пароль" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} /></label></div>
        {login.error && <div className="error-banner" role="alert">{errorText(login.error)}</div>}
        <button className="primary login-submit" aria-label="Войти в DealerOS" disabled={login.isPending}>{login.isPending ? <><span className="button-spinner" />Входим…</> : <>Войти в DealerOS <span>→</span></>}</button>
        <div className="demo-credentials"><span><strong>Демо-доступ</strong><small>admin@volga-auto.demo</small></span><span><strong>Пароль</strong><small>DealerOS!2026</small></span></div>
      </form></section>
    </main>
  }

  const session = getSession()
  const permissions = session?.permissions ?? []
  const permissionAllows = (required?: string | string[]) => !required || !session?.permissions || (Array.isArray(required) ? required.some((permission) => permissions.includes(permission)) : permissions.includes(required))
  const visibleNavigation = navigation.map((group) => ({ ...group, items: group.items.filter((item) => permissionAllows(item.permission)) })).filter((group) => group.items.length)
  const currentVehicle = selected ?? vehicles.data?.[0] ?? null
  const canCreate = permissionAllows('vehicles.create')
  const canAccept = permissionAllows('vehicles.accept')
  const canInspect = permissionAllows('vehicles.inspections.view')
  const canViewPhotos = permissionAllows('vehicles.photos.view')
  const canUploadPhotos = permissionAllows('vehicles.photos.upload')
  const canManagePhotos = permissionAllows('vehicles.photos.manage')
  const navigate = (next: View) => { if (next === 'inspections') setInspectionVehicle(null); setView(next); setMobileMenuOpen(false) }
  const logout = () => { clearSession(); setView('dashboard'); setAuthenticated(false); setMobileMenuOpen(false); queryClient.clear() }

  return <div className={`app-shell ${sidebarCompact ? 'sidebar-compact' : ''} ${mobileMenuOpen ? 'mobile-menu-open' : ''}`}>
    <aside className="sidebar">
      <div className="sidebar-brand"><AppLogo compact={sidebarCompact} inverse /><button className="sidebar-collapse" aria-label={sidebarCompact ? 'Развернуть меню' : 'Свернуть меню'} onClick={() => setSidebarCompact((value) => !value)}><Icon name="chevron" size={16} /></button><button className="sidebar-mobile-close" aria-label="Закрыть меню" onClick={() => setMobileMenuOpen(false)}><Icon name="close" /></button></div>
      <nav className="main-nav" aria-label="Разделы">{visibleNavigation.map((group) => <div className="nav-group" key={group.label}><span className="nav-group-label">{group.label}</span>{group.items.map((item) => <button aria-label={item.label} title={sidebarCompact ? item.label : undefined} key={item.view} className={view === item.view ? 'active' : ''} aria-current={view === item.view ? 'page' : undefined} onClick={() => navigate(item.view)}><Icon name={item.icon} size={19} /><span>{sidebarCompact ? item.shortLabel ?? item.label : item.label}</span>{view === item.view && <i />}</button>)}</div>)}</nav>
      <div className="sidebar-footer"><span className="system-status"><i />Система работает</span><small>DealerOS 1.0 · local MVP</small></div>
    </aside>
    <button className="mobile-menu-backdrop" aria-label="Закрыть меню" onClick={() => setMobileMenuOpen(false)} />

    <div className="app-main">
      <header className="topbar">
        <button className="mobile-menu-button" aria-label="Открыть меню" onClick={() => setMobileMenuOpen(true)}><Icon name="menu" /></button>
        <div className="topbar-title"><span>Рабочее пространство</span><strong>{viewTitles[view]}</strong></div>
        <div className="topbar-context"><span className="topbar-organization"><Icon name="building" size={17} /><span><small>{session?.organizationName}</small><strong>{session?.branchName}</strong></span></span><span className="topbar-divider" /><span className="topbar-profile"><span className="profile-avatar">{initials(session?.displayName)}</span><span><strong>{session?.displayName}</strong><small>{session?.email}</small></span></span><button className="logout-button" onClick={logout} aria-label="Выйти"><Icon name="logout" size={19} /><span>Выйти</span></button></div>
      </header>

      <main className="workspace">
        {view === 'dashboard' ? <Dashboard vehicles={vehicles.data ?? []} loading={vehicles.isLoading} permissions={permissions} onNavigate={navigate} onSelectVehicle={setSelected} />
          : view === 'inspections' ? <InspectionsWorkspace focusVehicle={inspectionVehicle} onClearFocus={() => setInspectionVehicle(null)} />
            : view === 'reconditioning' ? <ReconditioningWorkspace />
              : view === 'operations' ? <OperationsWorkspace />
                : view === 'quality-listings' ? <QualityListingsWorkspace />
                  : view === 'crm' ? <CrmWorkspace />
                    : view === 'sales' ? <SalesWorkspace />
                      : view === 'reservations' ? <ReservationsWorkspace />
                        : view === 'deals' ? <DealsWorkspace />
                          : view === 'finance' ? <FinanceWorkspace />
                            : <IntakeWorkspace branches={branches.data ?? []} branchesLoading={branches.isLoading} vehicles={vehicles.data ?? []} vehiclesLoading={vehicles.isLoading} currentVehicle={currentVehicle} selectedId={selected?.id} form={form} canCreate={canCreate} canAccept={canAccept} canInspect={canInspect} canViewPhotos={canViewPhotos} canUploadPhotos={canUploadPhotos} canManagePhotos={canManagePhotos} createVehicle={createVehicle} acceptVehicle={acceptVehicle} onSelect={setSelected} onOpenInspections={(vehicle) => { setInspectionVehicle(vehicle); setView('inspections') }} errors={branches.error ?? vehicles.error} />}
      </main>
    </div>
  </div>
}

function IntakeWorkspace({ branches, branchesLoading, vehicles, vehiclesLoading, currentVehicle, selectedId, form, canCreate, canAccept, canInspect, canViewPhotos, canUploadPhotos, canManagePhotos, createVehicle, acceptVehicle, onSelect, onOpenInspections, errors }: {
  branches: Branch[]; branchesLoading: boolean; vehicles: Vehicle[]; vehiclesLoading: boolean; currentVehicle: Vehicle | null; selectedId?: string
  form: ReturnType<typeof useForm<IntakeInput, unknown, IntakeForm>>; canCreate: boolean; canAccept: boolean; canInspect: boolean; canViewPhotos: boolean; canUploadPhotos: boolean; canManagePhotos: boolean
  createVehicle: ReturnType<typeof useMutation<Vehicle, Error, IntakeForm>>; acceptVehicle: ReturnType<typeof useMutation<Vehicle, Error, string>>
  onSelect: (vehicle: Vehicle) => void; onOpenInspections: (vehicle: Vehicle) => void; errors: unknown
}) {
  return <>
    <PageHeader eyebrow="Автомобили" title="Приёмка и реестр" description="Создайте цифровой паспорт, подтвердите поступление и продолжите работу с карточкой автомобиля.">
      <div className="metric"><span>Ожидают приёмки</span><strong>{vehicles.filter((vehicle) => vehicle.status === 'IntakeDraft').length}</strong><small>черновиков поступления</small></div>
    </PageHeader>

    {errors && <div className="error-banner workspace-error" role="alert">{errorText(errors)}</div>}

    <div className="content-grid intake-grid">
      <section className="panel intake-panel">
        <div className="panel-title"><span className="step">01</span><div><p className="eyebrow">Новое поступление</p><h2>Данные автомобиля</h2><p>Организация определяется из сессии. Филиал проверяется сервером.</p></div></div>
        {canCreate ? <form className="intake-form" onSubmit={form.handleSubmit((values) => createVehicle.mutate(values))}>
          <fieldset><legend>Идентификация</legend><label className="full">Филиал<select aria-label="Филиал" disabled={branchesLoading} {...form.register('branchId')}><option value="">{branchesLoading ? 'Загружаем филиалы…' : 'Выберите филиал'}</option>{branches.map((branch) => <option key={branch.id} value={branch.id}>{branch.name}</option>)}</select><FieldError message={form.formState.errors.branchId?.message} /></label><label className="full">VIN<input aria-label="VIN" placeholder="17 символов" maxLength={17} className="mono" {...form.register('vin')} /><FieldError message={form.formState.errors.vin?.message} /></label></fieldset>
          <fieldset><legend>Характеристики</legend><label>Марка<input aria-label="Марка" placeholder="Например, Toyota" maxLength={100} {...form.register('make')} /><FieldError message={form.formState.errors.make?.message} /></label><label>Модель<input aria-label="Модель" placeholder="Например, Camry" maxLength={100} {...form.register('model')} /><FieldError message={form.formState.errors.model?.message} /></label><label>Год<input aria-label="Год" type="number" {...form.register('year')} /><FieldError message={form.formState.errors.year?.message} /></label><label>Пробег, км<input aria-label="Пробег" type="number" {...form.register('mileageKm')} /><FieldError message={form.formState.errors.mileageKm?.message} /></label></fieldset>
          <fieldset><legend>Закупка</legend><label className="full">Плановая цена закупки<div className="money-input"><input aria-label="Плановая цена закупки" type="number" step="0.01" {...form.register('plannedPurchaseAmount')} /><span>₽</span></div><FieldError message={form.formState.errors.plannedPurchaseAmount?.message} /></label></fieldset>
          {createVehicle.error && <div className="error-banner full" role="alert">{errorText(createVehicle.error)}</div>}
          <div className="form-actions full"><span><strong>Следующий шаг</strong><small>Подтвердить фактическую приёмку на склад</small></span><button className="primary" disabled={createVehicle.isPending}>{createVehicle.isPending ? <><span className="button-spinner" />Сохраняем…</> : 'Создать поступление'}</button></div>
        </form> : <EmptyState icon="intake" title="Приёмка доступна только для чтения" description="Ваши права позволяют просматривать реестр, но не создавать поступления." />}
      </section>

      <section className="panel vehicle-panel">
        <div className="panel-title"><span className="step">02</span><div><p className="eyebrow">Выбранный объект</p><h2>Карточка автомобиля</h2><p>Статус, ключевые данные и следующий доступный шаг.</p></div></div>
        {!currentVehicle ? <EmptyState icon="car" title="Автомобиль не выбран" description="Создайте поступление или выберите строку в реестре ниже." /> : <VehicleCard vehicle={currentVehicle} accepting={acceptVehicle.isPending} canAccept={canAccept} canInspect={canInspect} onAccept={() => acceptVehicle.mutate(currentVehicle.id)} onOpenInspections={() => onOpenInspections(currentVehicle)} error={acceptVehicle.error} />}
      </section>
    </div>

    {currentVehicle && canViewPhotos && <section className="panel vehicle-gallery-panel"><VehiclePhotoGallery vehicleId={currentVehicle.id} canUpload={canUploadPhotos} canManage={canManagePhotos} /></section>}

    <section className="registry vehicle-registry">
      <div className="registry-heading"><div><p className="eyebrow">Автопарк</p><h2>Реестр автомобилей</h2><p className="muted">{vehicles.length} автомобилей в доступных филиалах</p></div><div className="registry-search"><Icon name="search" size={17} /><span>Выберите автомобиль в таблице</span></div></div>
      {vehiclesLoading ? <div className="loading-state" role="status"><span className="spinner" />Загружаем реестр…</div> : vehicles.length === 0 ? <EmptyState title="Реестр пока пуст" description="Первый автомобиль появится после создания поступления." /> : <div className="table-wrap"><table><thead><tr><th>Автомобиль</th><th>VIN</th><th>Филиал</th><th className="numeric">Цена закупки</th><th>Статус</th><th aria-label="Открыть" /></tr></thead><tbody>{vehicles.map((vehicle) => <tr key={vehicle.id} className={selectedId === vehicle.id ? 'selected' : ''} onClick={() => onSelect(vehicle)}><td><div className="registry-vehicle-cell"><VehicleCover compact vehicleId={vehicle.id} photoId={vehicle.coverPhotoId} name={`${vehicle.make} ${vehicle.model}`} /><span><strong>{vehicle.make} {vehicle.model}</strong><small>{vehicle.year} · {vehicle.mileageKm.toLocaleString('ru-RU')} км</small></span></div></td><td className="mono">{vehicle.vin}</td><td>{vehicle.branchName}</td><td className="numeric">{vehicle.plannedPurchaseAmount.toLocaleString('ru-RU')} ₽</td><td><Status status={vehicle.status} /></td><td><button className="table-row-action" aria-label={`Открыть ${vehicle.make} ${vehicle.model}`} onClick={(event) => { event.stopPropagation(); onSelect(vehicle) }}><Icon name="chevron" size={16} /></button></td></tr>)}</tbody></table></div>}
    </section>
  </>
}

function VehicleCard({ vehicle, accepting, canAccept, canInspect, onAccept, onOpenInspections, error }: { vehicle: Vehicle; accepting: boolean; canAccept: boolean; canInspect: boolean; onAccept: () => void; onOpenInspections: () => void; error: unknown }) {
  return <div className="vehicle-card">
    <VehicleCover vehicleId={vehicle.id} photoId={vehicle.coverPhotoId} name={`${vehicle.make} ${vehicle.model}`} />
    <div className="vehicle-card-top"><Status status={vehicle.status} /><span className="stock-number">{vehicle.stockNumber ?? 'Номер после приёмки'}</span></div>
    <div className="vehicle-identity"><h3>{vehicle.make} {vehicle.model}</h3><p>{vehicle.year} · {vehicle.mileageKm.toLocaleString('ru-RU')} км</p><code>{vehicle.vin}</code></div>
    <dl><div><dt>Закупка</dt><dd>{vehicle.plannedPurchaseAmount.toLocaleString('ru-RU')} ₽</dd></div><div><dt>Филиал</dt><dd>{vehicle.branchName}</dd></div><div><dt>Год</dt><dd>{vehicle.year}</dd></div><div><dt>Пробег</dt><dd>{vehicle.mileageKm.toLocaleString('ru-RU')} км</dd></div></dl>
    <div className="journey-block"><div><span className="overline">Путь автомобиля</span><small>Текущий этап выделен</small></div><VehicleJourney status={vehicle.status} /></div>
    {error ? <div className="error-banner" role="alert">{errorText(error)}</div> : null}
    {vehicle.status === 'IntakeDraft' ? canAccept ? <button className="accept-button" onClick={onAccept} disabled={accepting}>{accepting ? 'Принимаем…' : 'Принять на склад'}<span>→</span></button> : <div className="info-banner">Для приёмки на склад требуется право <code>vehicles.accept</code>.</div> : <div className="vehicle-next-action" role={vehicle.status === 'InStock' ? 'status' : undefined}><span><strong>{vehicle.status === 'InStock' ? 'Автомобиль принят на склад' : 'Автомобиль в работе'}</strong><small>{vehicle.status === 'InStock' ? `Складской номер ${vehicle.stockNumber ?? 'назначается'} · следующий шаг — технический осмотр` : 'Продолжите работу в соответствующем разделе'}</small></span>{canInspect && <button className="secondary" onClick={onOpenInspections}>Открыть осмотры <Icon name="chevron" size={15} /></button>}</div>}
  </div>
}

function Status({ status }: { status: Vehicle['status'] }) {
  const labels: Record<Vehicle['status'], string> = { IntakeDraft: 'Черновик', InStock: 'На складе', InspectionInProgress: 'На осмотре', ReconditioningRequired: 'Нужна подготовка', InspectionPassed: 'Осмотр пройден', ReadyForSale: 'Готов к продаже', Reserved: 'Забронирован', SaleInProgress: 'Сделка', Sold: 'Продан' }
  const tone = status === 'IntakeDraft' ? 'draft' : status === 'ReconditioningRequired' ? 'warning' : status === 'SaleInProgress' || status === 'Reserved' ? 'info' : 'success'
  return <span className={`status ${tone}`}><i />{labels[status]}</span>
}

function FieldError({ message }: { message?: string }) { return message ? <span className="field-error">{message}</span> : <span className="field-error-placeholder" aria-hidden="true" /> }

function initials(name?: string) {
  const value = name?.trim() || 'Пользователь'
  return value.split(/\s+/).slice(0, 2).map((part) => part[0]?.toUpperCase()).join('')
}
