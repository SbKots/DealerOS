import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { api, ApiError, clearSession, getSession, hasSession, saveSession, sessionExpiredEvent, type Session } from './api'
import { InspectionsWorkspace } from './Inspections'
import { ReconditioningWorkspace } from './Reconditioning'
import { OperationsWorkspace } from './Operations'
import { QualityListingsWorkspace } from './QualityListings'
import { CrmWorkspace } from './Crm'
import { SalesWorkspace } from './Sales'
import { ReservationsWorkspace } from './Reservations'
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
  stockNumber?: string; createdAt: string; acceptedAt?: string; version: number
}

function errorText(error: unknown) {
  return error instanceof ApiError ? error.message : 'Не удалось выполнить операцию. Повторите попытку.'
}

export default function App() {
  const queryClient = useQueryClient()
  const [authenticated, setAuthenticated] = useState(hasSession())
  const [selected, setSelected] = useState<Vehicle | null>(null)
  const [view, setView] = useState<'intake' | 'inspections' | 'reconditioning' | 'operations' | 'quality-listings' | 'crm' | 'sales' | 'reservations'>('intake')
  const [inspectionVehicle, setInspectionVehicle] = useState<Vehicle | null>(null)
  const [email, setEmail] = useState('admin@volga-auto.demo')
  const [password, setPassword] = useState('DealerOS!2026')

  const branches = useQuery({ queryKey: ['branches'], queryFn: () => api<Branch[]>('/api/branches'), enabled: authenticated })
  const vehicles = useQuery({ queryKey: ['vehicles'], queryFn: () => api<Vehicle[]>('/api/vehicles'), enabled: authenticated })

  const form = useForm<IntakeInput, unknown, IntakeForm>({
    resolver: zodResolver(intakeSchema),
    defaultValues: { vin: '', make: '', model: '', year: 2022, mileageKm: 0, plannedPurchaseAmount: 0, currency: 'RUB' },
  })

  useEffect(() => {
    if (branches.data?.length && !form.getValues('branchId')) {
      form.setValue('branchId', branches.data[0].id, { shouldValidate: true })
    }
  }, [branches.data, form])

  useEffect(() => {
    const handleExpiredSession = () => {
      setAuthenticated(false)
      setSelected(null)
      queryClient.clear()
    }
    window.addEventListener(sessionExpiredEvent, handleExpiredSession)
    return () => window.removeEventListener(sessionExpiredEvent, handleExpiredSession)
  }, [queryClient])

  const login = useMutation({
    mutationFn: () => api<Session>('/api/auth/login', { method: 'POST', body: { email, password }, authenticated: false }),
    onSuccess: (session) => { saveSession(session); setView('intake'); setAuthenticated(true) },
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
      <section className="login-panel">
        <div className="brand"><span className="brand-mark">D</span><span>DealerOS</span></div>
        <p className="eyebrow">Операционная система автосалона</p>
        <h1>Войдите в рабочее пространство</h1>
        <p className="muted">Демо-доступ уже заполнен. Организация и филиалы будут определены из защищённой сессии.</p>
        <label>Email<input aria-label="Email" value={email} onChange={(e) => setEmail(e.target.value)} /></label>
        <label>Пароль<input aria-label="Пароль" type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></label>
        {login.error && <div className="error-banner" role="alert">{errorText(login.error)}</div>}
        <button className="primary" onClick={() => login.mutate()} disabled={login.isPending}>{login.isPending ? 'Входим…' : 'Войти в DealerOS'}</button>
      </section>
      <aside className="login-aside"><span>Первый вертикальный срез</span><strong>Поступление автомобиля без Excel и потери истории</strong></aside>
    </main>
  }

  const currentVehicle = selected ?? vehicles.data?.[0] ?? null
  const session = getSession()
  return <div className="app-shell">
    <header>
      <div className="brand"><span className="brand-mark">D</span><span>DealerOS</span></div>
      <nav className="main-nav" aria-label="Разделы">
        <button className={view === 'intake' ? 'active' : ''} onClick={() => setView('intake')}>Приёмка</button>
        {session?.permissions?.includes('vehicles.inspections.view') && <button className={view === 'inspections' ? 'active' : ''} onClick={() => { setInspectionVehicle(null); setView('inspections') }}>Осмотры</button>}
        {session?.permissions?.includes('reconditioning.view') && <button className={view === 'reconditioning' ? 'active' : ''} onClick={() => setView('reconditioning')}>Подготовка</button>}
        {session?.permissions?.includes('operations.view') && <button className={view === 'operations' ? 'active' : ''} onClick={() => setView('operations')}>Выполнение</button>}
        {(session?.permissions?.includes('quality.view') || session?.permissions?.includes('listings.view')) && <button className={view === 'quality-listings' ? 'active' : ''} onClick={() => setView('quality-listings')}>Качество и контент</button>}
        {(session?.permissions?.includes('crm.customers.view') || session?.permissions?.includes('crm.leads.view')) && <button className={view === 'crm' ? 'active' : ''} onClick={() => setView('crm')}>Клиенты и лиды</button>}
        {(session?.permissions?.includes('sales.visits.view') || session?.permissions?.includes('sales.offers.view')) && <button className={view === 'sales' ? 'active' : ''} onClick={() => setView('sales')}>Визиты и Offer</button>}
        {session?.permissions?.includes('reservations.view') && <button className={view === 'reservations' ? 'active' : ''} onClick={() => setView('reservations')}>Брони</button>}
      </nav>
      <div className="context-pill"><span className="pulse" />{session?.organizationName} · {session?.branchName}</div>
      <button className="link-button" onClick={() => { clearSession(); setView('intake'); setAuthenticated(false); queryClient.clear() }}>Выйти</button>
    </header>

    <main className="workspace">
      {view === 'inspections' ? <InspectionsWorkspace focusVehicle={inspectionVehicle} onClearFocus={() => setInspectionVehicle(null)} /> : view === 'reconditioning' ? <ReconditioningWorkspace /> : view === 'operations' ? <OperationsWorkspace /> : view === 'quality-listings' ? <QualityListingsWorkspace /> : view === 'crm' ? <CrmWorkspace /> : view === 'sales' ? <SalesWorkspace /> : view === 'reservations' ? <ReservationsWorkspace /> : <>
      <div className="page-heading">
        <div><p className="eyebrow">Склад автомобилей</p><h1>Приём автомобиля</h1><p className="muted">Создайте цифровой паспорт, затем подтвердите фактическую приёмку на площадку.</p></div>
        <div className="metric"><span>На контроле</span><strong>{vehicles.data?.filter((x) => x.status === 'IntakeDraft').length ?? 0}</strong><small>черновиков поступления</small></div>
      </div>

      {(branches.error || vehicles.error) && <div className="error-banner workspace-error" role="alert">
        {errorText(branches.error ?? vehicles.error)}
      </div>}

      <div className="content-grid">
        <section className="panel intake-panel">
          <div className="panel-title"><span className="step">01</span><div><h2>Данные поступления</h2><p>Организация берётся из сессии и не передаётся формой.</p></div></div>
          <form onSubmit={form.handleSubmit((values) => createVehicle.mutate(values))}>
            <label className="full">Филиал<select aria-label="Филиал" {...form.register('branchId')}>
              <option value="">Выберите филиал</option>{branches.data?.map((branch) => <option key={branch.id} value={branch.id}>{branch.name}</option>)}
            </select><FieldError message={form.formState.errors.branchId?.message} /></label>
            <label className="full">VIN<input aria-label="VIN" placeholder="17 символов" maxLength={17} className="mono" {...form.register('vin')} /><FieldError message={form.formState.errors.vin?.message} /></label>
            <label>Марка<input aria-label="Марка" placeholder="Например, Toyota" maxLength={100} {...form.register('make')} /><FieldError message={form.formState.errors.make?.message} /></label>
            <label>Модель<input aria-label="Модель" placeholder="Например, Camry" maxLength={100} {...form.register('model')} /><FieldError message={form.formState.errors.model?.message} /></label>
            <label>Год<input aria-label="Год" type="number" {...form.register('year')} /><FieldError message={form.formState.errors.year?.message} /></label>
            <label>Пробег, км<input aria-label="Пробег" type="number" {...form.register('mileageKm')} /><FieldError message={form.formState.errors.mileageKm?.message} /></label>
            <label className="full">Плановая цена закупки<div className="money-input"><input aria-label="Плановая цена закупки" type="number" step="0.01" {...form.register('plannedPurchaseAmount')} /><span>₽</span></div><FieldError message={form.formState.errors.plannedPurchaseAmount?.message} /></label>
            {createVehicle.error && <div className="error-banner full" role="alert">{errorText(createVehicle.error)}</div>}
            <button className="primary full" disabled={createVehicle.isPending}>{createVehicle.isPending ? 'Сохраняем…' : 'Создать поступление'}</button>
          </form>
        </section>

        <section className="panel vehicle-panel">
          <div className="panel-title"><span className="step">02</span><div><h2>Карточка автомобиля</h2><p>Проверка перед фактической приёмкой.</p></div></div>
          {!currentVehicle ? <div className="empty-state"><div className="car-outline">◇</div><strong>Карточка появится здесь</strong><p>Заполните обязательные поля поступления слева.</p></div> : <VehicleCard vehicle={currentVehicle} accepting={acceptVehicle.isPending} onAccept={() => acceptVehicle.mutate(currentVehicle.id)} onOpenInspections={() => { setInspectionVehicle(currentVehicle); setView('inspections') }} error={acceptVehicle.error} />}
        </section>
      </div>

      <section className="registry">
        <div><p className="eyebrow">Последние операции</p><h2>Реестр автомобилей</h2></div>
        {vehicles.isLoading ? <p>Загрузка…</p> : <div className="table-wrap"><table><thead><tr><th>Автомобиль</th><th>VIN</th><th>Филиал</th><th>Цена закупки</th><th>Статус</th></tr></thead><tbody>
          {vehicles.data?.map((vehicle) => <tr key={vehicle.id} onClick={() => setSelected(vehicle)}><td><strong>{vehicle.make} {vehicle.model}</strong><small>{vehicle.year} · {vehicle.mileageKm.toLocaleString('ru-RU')} км</small></td><td className="mono">{vehicle.vin}</td><td>{vehicle.branchName}</td><td>{vehicle.plannedPurchaseAmount.toLocaleString('ru-RU')} ₽</td><td><Status status={vehicle.status} /></td></tr>)}
        </tbody></table></div>}
      </section>
      </>}
    </main>
  </div>
}

function VehicleCard({ vehicle, accepting, onAccept, onOpenInspections, error }: { vehicle: Vehicle; accepting: boolean; onAccept: () => void; onOpenInspections: () => void; error: unknown }) {
  return <div className="vehicle-card">
    <div className="vehicle-card-top"><Status status={vehicle.status} /><span className="mono">{vehicle.stockNumber ?? 'Номер после приёмки'}</span></div>
    <h3>{vehicle.make} {vehicle.model}</h3><p className="vin mono">{vehicle.vin}</p>
    <dl><div><dt>Год</dt><dd>{vehicle.year}</dd></div><div><dt>Пробег</dt><dd>{vehicle.mileageKm.toLocaleString('ru-RU')} км</dd></div><div><dt>Закупка</dt><dd>{vehicle.plannedPurchaseAmount.toLocaleString('ru-RU')} ₽</dd></div><div><dt>Филиал</dt><dd>{vehicle.branchName}</dd></div></dl>
    <div className="rule-check"><span>✓</span><p><strong>Обязательные данные проверены сервером</strong><small>VIN уникален в организации, филиал доступен сотруднику.</small></p></div>
    {error ? <div className="error-banner" role="alert">{errorText(error)}</div> : null}
    {vehicle.status === 'IntakeDraft' ? <button className="accept-button" onClick={onAccept} disabled={accepting}>{accepting ? 'Принимаем…' : 'Принять на склад'}<span>→</span></button> : <><div className="success-banner" role="status"><span>✓</span><div><strong>{vehicle.status === 'InStock' ? 'Автомобиль принят на склад' : 'Автомобиль в работе'}</strong><small>{vehicle.stockNumber}</small></div></div><button className="tab-button" onClick={onOpenInspections}>Осмотры →</button></>}
  </div>
}

function Status({ status }: { status: Vehicle['status'] }) {
  const labels: Record<Vehicle['status'], string> = { IntakeDraft: 'Черновик', InStock: 'На складе', InspectionInProgress: 'На осмотре', ReconditioningRequired: 'Нужна подготовка', InspectionPassed: 'Осмотр пройден', ReadyForSale: 'Готов к продаже', Reserved: 'Забронирован', SaleInProgress: 'Сделка', Sold: 'Продан' }
  return <span className={`status ${status === 'IntakeDraft' ? 'draft' : 'success'}`}><i />{labels[status]}</span>
}

function FieldError({ message }: { message?: string }) { return message ? <span className="field-error">{message}</span> : null }
