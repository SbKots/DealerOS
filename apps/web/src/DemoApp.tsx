import { useState } from 'react'
import { AppLogo, Icon, Money, PageHeader, VehicleJourney, VehicleVisual, type IconName } from './ui'
import './App.css'

type DemoView = 'dashboard' | 'vehicles' | 'vehicle' | 'inspections' | 'reconditioning' | 'crm' | 'sales' | 'reservations' | 'deals' | 'finance'
type DemoNavItem = { view: Exclude<DemoView, 'vehicle'>; label: string; icon: IconName }
type DemoVehicle = { id: string; make: string; model: string; year: number; mileage: number; vin: string; stock: string; status: string; tone: 'success' | 'warning' | 'info'; price: number }

const demoVehicles: DemoVehicle[] = [
  { id: 'demo-vesta', make: 'Lada', model: 'Vesta', year: 2023, mileage: 18_400, vin: 'DEMO0000000000001', stock: 'MSK-0241', status: 'Готов к продаже', tone: 'success', price: 1_420_000 },
  { id: 'demo-kodiaq', make: 'Skoda', model: 'Kodiaq', year: 2021, mileage: 61_200, vin: 'DEMO0000000000002', stock: 'MSK-0238', status: 'Требуется подготовка', tone: 'warning', price: 2_780_000 },
  { id: 'demo-camry', make: 'Toyota', model: 'Camry', year: 2022, mileage: 34_800, vin: 'DEMO0000000000003', stock: 'MSK-0245', status: 'На осмотре', tone: 'info', price: 3_150_000 },
]

const demoNavigation: { label: string; items: DemoNavItem[] }[] = [
  { label: 'Главное', items: [{ view: 'dashboard', label: 'Обзор', icon: 'dashboard' }] },
  { label: 'Автомобили', items: [
    { view: 'vehicles', label: 'Автомобили', icon: 'intake' },
    { view: 'inspections', label: 'Осмотры', icon: 'inspection' },
    { view: 'reconditioning', label: 'Подготовка', icon: 'tools' },
  ] },
  { label: 'Продажи', items: [
    { view: 'crm', label: 'Клиенты и обращения', icon: 'customers' },
    { view: 'sales', label: 'Визиты и предложения', icon: 'sales' },
    { view: 'reservations', label: 'Бронирования', icon: 'reservation' },
    { view: 'deals', label: 'Сделки', icon: 'deal' },
  ] },
  { label: 'Аналитика', items: [{ view: 'finance', label: 'Экономика', icon: 'finance' }] },
]

const viewTitles: Record<DemoView, string> = {
  dashboard: 'Обзор', vehicles: 'Автомобили', vehicle: 'Карточка автомобиля', inspections: 'Осмотры', reconditioning: 'Подготовка', crm: 'Клиенты и обращения', sales: 'Визиты и предложения', reservations: 'Бронирования', deals: 'Сделки', finance: 'Экономика',
}

export default function DemoApp() {
  const [entered, setEntered] = useState(false)
  const [view, setView] = useState<DemoView>('dashboard')
  const [selectedVehicle, setSelectedVehicle] = useState(demoVehicles[0])
  const [sidebarCompact, setSidebarCompact] = useState(false)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
  const [completedWorks, setCompletedWorks] = useState<string[]>(['Диагностика ходовой'])

  if (!entered) return <DemoLogin onEnter={() => setEntered(true)} />

  const navigate = (next: DemoView) => { setView(next); setMobileMenuOpen(false) }
  const openVehicle = (vehicle: DemoVehicle) => { setSelectedVehicle(vehicle); navigate('vehicle') }
  const toggleWork = (work: string) => setCompletedWorks((current) => current.includes(work) ? current.filter((item) => item !== work) : [...current, work])

  return <div className={`app-shell demo-shell ${sidebarCompact ? 'sidebar-compact' : ''} ${mobileMenuOpen ? 'mobile-menu-open' : ''}`} data-demo-mode="true">
    <aside className="sidebar">
      <div className="sidebar-brand">
        <AppLogo compact={sidebarCompact} inverse />
        <button className="sidebar-collapse" aria-label={sidebarCompact ? 'Развернуть меню' : 'Свернуть меню'} onClick={() => setSidebarCompact((value) => !value)}><Icon name="chevron" size={16} /></button>
        <button className="sidebar-mobile-close" aria-label="Закрыть меню" onClick={() => setMobileMenuOpen(false)}><Icon name="close" /></button>
      </div>
      <nav className="main-nav" aria-label="Разделы">{demoNavigation.map((group) => <div className="nav-group" key={group.label}>
        <span className="nav-group-label">{group.label}</span>
        {group.items.map((item) => <button type="button" aria-label={item.label} title={sidebarCompact ? item.label : undefined} key={item.view} className={view === item.view || (item.view === 'vehicles' && view === 'vehicle') ? 'active' : ''} aria-current={view === item.view ? 'page' : undefined} onClick={() => navigate(item.view)}><Icon name={item.icon} size={19} /><span>{item.label}</span>{(view === item.view || (item.view === 'vehicles' && view === 'vehicle')) && <i />}</button>)}
      </div>)}</nav>
      <div className="sidebar-footer"><span className="system-status"><i />Демонстрационный режим</span><small>Только синтетические данные</small></div>
    </aside>
    <button className="mobile-menu-backdrop" aria-label="Закрыть меню" onClick={() => setMobileMenuOpen(false)} />

    <div className="app-main">
      <header className="topbar">
        <button className="mobile-menu-button" aria-label="Открыть меню" onClick={() => setMobileMenuOpen(true)}><Icon name="menu" /></button>
        <div className="topbar-title"><span>Визуальная демоверсия</span><strong>{viewTitles[view]}</strong></div>
        <div className="topbar-context"><span className="topbar-organization"><Icon name="building" size={17} /><span><small>Volga Auto Demo</small><strong>Москва — демо-площадка</strong></span></span><span className="topbar-divider" /><span className="topbar-profile"><span className="profile-avatar">ДМ</span><span><strong>Демо-менеджер</strong><small>demo@dealeros.example</small></span></span><button className="logout-button" onClick={() => setEntered(false)} aria-label="Выйти"><Icon name="logout" size={19} /><span>Выйти</span></button></div>
      </header>
      <div className="demo-mode-banner" role="status"><Icon name="alert" size={16} /><span><strong>Демонстрационный режим</strong> · синтетические данные · изменения хранятся только до обновления страницы</span></div>
      <main className="workspace demo-workspace">
        {view === 'dashboard' && <DemoDashboard onNavigate={navigate} onOpenVehicle={() => openVehicle(demoVehicles[1])} />}
        {view === 'vehicles' && <DemoVehicles onOpen={openVehicle} />}
        {view === 'vehicle' && <DemoVehicleCard vehicle={selectedVehicle} onBack={() => navigate('vehicles')} />}
        {view === 'inspections' && <DemoInspections />}
        {view === 'reconditioning' && <DemoReconditioning completed={completedWorks} onToggle={toggleWork} />}
        {view === 'crm' && <DemoCrm />}
        {view === 'sales' && <DemoSales />}
        {view === 'reservations' && <DemoReservations />}
        {view === 'deals' && <DemoDeals />}
        {view === 'finance' && <DemoFinance />}
      </main>
    </div>
  </div>
}

function DemoLogin({ onEnter }: { onEnter: () => void }) {
  return <main className="login-shell demo-login">
    <section className="login-story">
      <AppLogo inverse />
      <div className="login-story-copy"><p className="eyebrow">DealerOS 1.0</p><h1>Весь путь автомобиля.<br />В одном рабочем пространстве.</h1><p>Безопасная статическая демоверсия с синтетическими данными — для знакомства с интерфейсом без подключения к дилерской инфраструктуре.</p></div>
      <div className="login-route" aria-label="Путь автомобиля"><span>Поступление</span><i /><span>Подготовка</span><i /><span>Продажа</span><i /><span>Экономика</span></div>
      <svg className="login-car" viewBox="0 0 720 250" aria-hidden="true"><path d="M82 174c31-9 55-59 95-95 25-23 60-31 126-35 101-6 178 10 231 43 25 16 48 48 70 78l56 13c18 4 29 17 29 39v12H35v-20c0-16 10-28 26-32z" /><path d="m190 85 109-13c75-8 148 1 197 27 18 10 34 27 51 51H145z" /><circle cx="173" cy="207" r="49" /><circle cx="554" cy="207" r="49" /></svg>
    </section>
    <section className="login-panel-wrap"><div className="login-panel">
      <div className="login-mobile-logo"><AppLogo /></div>
      <span className="demo-badge">Статическая демоверсия</span>
      <div><p className="eyebrow">Безопасный просмотр</p><h2>Откройте DealerOS</h2><p className="muted">Сайт работает без API, базы данных и облачного хранилища. Все показанные автомобили и клиенты вымышлены.</p></div>
      <div className="demo-login-points"><span><Icon name="quality" size={18} />Без реальных данных</span><span><Icon name="operations" size={18} />Основные экраны доступны</span><span><Icon name="finance" size={18} />Действия работают локально</span></div>
      <button type="button" className="primary login-submit" aria-label="Открыть демоверсию DealerOS" onClick={onEnter}>Открыть демоверсию <span>→</span></button>
    </div></section>
  </main>
}

function DemoDashboard({ onNavigate, onOpenVehicle }: { onNavigate: (view: DemoView) => void; onOpenVehicle: () => void }) {
  return <>
    <PageHeader eyebrow="Рабочий день" title="Обзор" description="Главные показатели демонстрационного автосалона — только синтетические данные."><button className="primary" onClick={() => onNavigate('vehicles')}><Icon name="car" size={17} />Автомобили</button></PageHeader>
    <section className="dashboard-hero"><div className="dashboard-hero-copy"><span className="overline">Автопарк сегодня</span><strong>24</strong><p>автомобиля на демо-площадке</p></div><div className="stage-overview">{[['3', 'Поступление', 'vehicles'], ['2', 'Осмотр', 'inspections'], ['5', 'Подготовка', 'reconditioning'], ['8', 'Готовы к продаже', 'vehicles'], ['4', 'Продажи', 'sales'], ['2', 'Продано', 'finance']].map(([value, label, target]) => <button key={label} onClick={() => onNavigate(target as DemoView)}><span>{value}</span><small>{label}</small><Icon name="chevron" size={15} /></button>)}</div></section>
    <div className="dashboard-metrics"><DemoMetric icon="alert" label="Требуют внимания" value="7" tone="warning" /><DemoMetric icon="customers" label="Новые обращения" value="4" /><DemoMetric icon="reservation" label="Активные брони" value="2" /><DemoMetric icon="deal" label="Сделки в работе" value="3" /></div>
    <div className="dashboard-grid"><section className="surface dashboard-attention"><div className="surface-heading"><div><span className="overline">Приоритет</span><h2>Автомобили, требующие действий</h2></div></div><div className="attention-list"><button onClick={onOpenVehicle}><VehicleVisual name="Skoda Kodiaq" /><span><strong>Skoda Kodiaq</strong><small>2021 · Москва — демо-площадка</small><code>DEMO0000000000002</code></span><span className="attention-status warning">Создать план</span><Icon name="chevron" size={17} /></button><button onClick={() => onNavigate('inspections')}><VehicleVisual name="Toyota Camry" /><span><strong>Toyota Camry</strong><small>2022 · Москва — демо-площадка</small><code>DEMO0000000000003</code></span><span className="attention-status">Завершить осмотр</span><Icon name="chevron" size={17} /></button></div></section><section className="surface dashboard-agenda"><div className="surface-heading"><div><span className="overline">Расписание</span><h2>Ближайшие визиты</h2></div></div><div className="agenda-list"><article><time><strong>15:30</strong><small>сегодня</small></time><span><strong>Клиент Демо №1042</strong><small>Lada Vesta · тест-драйв</small></span></article><article><time><strong>17:00</strong><small>сегодня</small></time><span><strong>Клиент Демо №1047</strong><small>Skoda Kodiaq · консультация</small></span></article></div></section></div>
  </>
}

function DemoMetric({ icon, label, value, tone = '' }: { icon: IconName; label: string; value: string; tone?: string }) {
  return <article className={`dashboard-metric ${tone}`}><span className="metric-icon"><Icon name={icon} size={20} /></span><span><small>{label}</small><strong>{value}</strong><em>синтетический показатель</em></span><Icon name="chevron" size={16} /></article>
}

function DemoVehicles({ onOpen }: { onOpen: (vehicle: DemoVehicle) => void }) {
  return <><PageHeader eyebrow="Автомобили" title="Реестр автомобилей" description="Демонстрационный автопарк и текущий этап каждого автомобиля." />
    <section className="registry"><div className="registry-heading"><div><p className="eyebrow">Демо-площадка</p><h2>24 автомобиля</h2><p className="muted">Показаны три синтетические карточки</p></div></div><div className="table-wrap"><table><thead><tr><th>Автомобиль</th><th>VIN</th><th>Складской номер</th><th className="numeric">Цена</th><th>Статус</th><th /></tr></thead><tbody>{demoVehicles.map((vehicle) => <tr key={vehicle.id} onClick={() => onOpen(vehicle)}><td><strong>{vehicle.make} {vehicle.model}</strong><small>{vehicle.year} · {vehicle.mileage.toLocaleString('ru-RU')} км</small></td><td className="mono">{vehicle.vin}</td><td>{vehicle.stock}</td><td className="numeric"><Money value={vehicle.price} /></td><td><DemoStatus vehicle={vehicle} /></td><td><button className="table-row-action" aria-label={`Открыть ${vehicle.make} ${vehicle.model}`} onClick={() => onOpen(vehicle)}><Icon name="chevron" size={16} /></button></td></tr>)}</tbody></table></div></section></>
}

function DemoVehicleCard({ vehicle, onBack }: { vehicle: DemoVehicle; onBack: () => void }) {
  return <><PageHeader eyebrow="Карточка автомобиля" title={`${vehicle.make} ${vehicle.model}`} description={`${vehicle.year} · ${vehicle.stock} · синтетические данные`}><button className="secondary" onClick={onBack}>← К реестру</button></PageHeader><div className="demo-detail-grid"><section className="panel"><VehicleVisual name={`${vehicle.make} ${vehicle.model}`} /><div className="vehicle-card-top"><DemoStatus vehicle={vehicle} /><span className="stock-number">{vehicle.stock}</span></div><div className="vehicle-identity"><h3>{vehicle.make} {vehicle.model}</h3><p>{vehicle.year} · {vehicle.mileage.toLocaleString('ru-RU')} км</p><code>{vehicle.vin}</code></div><dl className="demo-data-list"><div><dt>Плановая цена</dt><dd><Money value={vehicle.price} /></dd></div><div><dt>Площадка</dt><dd>Москва — демо</dd></div><div><dt>Осмотр</dt><dd>14 июля 2026</dd></div><div><dt>Ответственный</dt><dd>Демо-менеджер</dd></div></dl></section><section className="panel"><p className="eyebrow">Путь автомобиля</p><h2>История и следующий шаг</h2><VehicleJourney status={vehicle.tone === 'warning' ? 'ReconditioningRequired' : vehicle.tone === 'info' ? 'InspectionInProgress' : 'ReadyForSale'} /><div className="timeline"><article><span>14:10</span><strong>Карточка обновлена</strong><p>Изменение сохранено в демонстрационном журнале.</p></article><article><span>12:40</span><strong>Осмотр завершён</strong><p>Результаты доступны сотрудникам площадки.</p></article><article><span>10:15</span><strong>Принят на склад</strong><p>Назначен складской номер {vehicle.stock}.</p></article></div></section></div></>
}

function DemoStatus({ vehicle }: { vehicle: DemoVehicle }) { return <span className={`status ${vehicle.tone}`}><i />{vehicle.status}</span> }

function DemoInspections() {
  return <><PageHeader eyebrow="Диагностика" title="Осмотры" description="Очередь осмотров, чек-лист и зафиксированные дефекты." /><div className="demo-detail-grid"><section className="panel"><div className="panel-title"><span className="step">01</span><div><p className="eyebrow">Активный осмотр</p><h2>Toyota Camry · MSK-0245</h2><p>12 из 14 пунктов заполнено</p></div></div><div className="demo-checklist"><span className="done">✓ Кузов и лакокрасочное покрытие</span><span className="done">✓ Электрооборудование</span><span className="done">✓ Салон и комплектация</span><span>○ Тормозная система</span><span>○ Финальное заключение</span></div></section><section className="panel"><p className="eyebrow">Дефекты</p><h2>Найдено 2 замечания</h2><article className="defect-card major"><div><span className="severity">Важно</span><strong>Скол на переднем бампере</strong></div><p>Требуется локальная окраска до публикации.</p></article><article className="defect-card"><div><span className="severity">Средне</span><strong>Износ передних колодок</strong></div><p>Рекомендована замена в плане подготовки.</p></article></section></div></>
}

function DemoReconditioning({ completed, onToggle }: { completed: string[]; onToggle: (work: string) => void }) {
  const works = [['Диагностика ходовой', 4_500], ['Локальная окраска бампера', 18_000], ['Замена тормозных колодок', 12_800]] as const
  return <><PageHeader eyebrow="Предпродажная подготовка" title="План работ" description="Состав работ, плановый бюджет и локальная демонстрация изменения статусов." /><div className="budget-grid"><DemoBudget label="Работы" value={35_300} /><DemoBudget label="Запчасти" value={9_800} /><DemoBudget label="Лимит" value={50_000} /><DemoBudget label="Резерв" value={4_900} /></div><section className="panel"><div className="surface-heading"><div><span className="overline">Skoda Kodiaq · ревизия 2</span><h2>Утверждённый план</h2></div><span className="status success"><i />Одобрен</span></div><div className="demo-work-list">{works.map(([name, price]) => <button key={name} className={completed.includes(name) ? 'complete' : ''} onClick={() => onToggle(name)}><span>{completed.includes(name) ? '✓' : '○'}</span><span><strong>{name}</strong><small>Демонстрационная работа · нажмите для смены статуса</small></span><Money value={price} /></button>)}</div></section></>
}

function DemoBudget({ label, value }: { label: string; value: number }) { return <article className="snapshot-card"><span>{label}</span><strong><Money value={value} /></strong><small>RUB · демонстрационный расчёт</small></article> }

function DemoCrm() {
  return <><PageHeader eyebrow="CRM" title="Клиенты и обращения" description="Синтетические клиенты, источники и ближайшие действия менеджеров." /><div className="demo-detail-grid"><section className="panel"><div className="surface-heading"><div><span className="overline">Клиенты</span><h2>Последние карточки</h2></div></div><div className="customer-list"><button className="customer-row selected"><span><strong>Клиент Демо №1042</strong><small>demo-1042@example.invalid · согласие зафиксировано</small></span><span className="status success"><i />Активен</span></button><button className="customer-row"><span><strong>Клиент Демо №1047</strong><small>+7 000 000-10-47 · только синтетические данные</small></span><span className="status info"><i />Новый</span></button></div></section><section className="panel"><div className="surface-heading"><div><span className="overline">Обращения</span><h2>В работе</h2></div></div><div className="lead-list"><button className="lead-row"><span><strong>Подбор семейного кроссовера</strong><small>Ответственный: Демо-менеджер</small></span><span>Сегодня, 16:00</span></button><button className="lead-row overdue"><span><strong>Обмен автомобиля по trade-in</strong><small>Следующее действие просрочено</small></span><span>− 45 мин</span></button></div></section></div></>
}

function DemoSales() {
  return <><PageHeader eyebrow="Продажи" title="Визиты и предложения" description="Календарь встреч и версии коммерческих предложений." /><div className="demo-detail-grid"><section className="panel"><p className="eyebrow">Календарь</p><h2>Визиты сегодня</h2><div className="calendar-list"><button><time>15:30–16:30</time><span><strong>Клиент Демо №1042</strong><small>Lada Vesta · тест-драйв</small></span><span className="status info"><i />Запланирован</span></button><button><time>17:00–17:45</time><span><strong>Клиент Демо №1047</strong><small>Skoda Kodiaq · консультация</small></span><span className="status success"><i />Подтверждён</span></button></div></section><section className="panel"><p className="eyebrow">Предложения</p><h2>Коммерческое предложение №D-0184</h2><div className="snapshot-card"><span>Финальная стоимость</span><strong><Money value={1_495_000} /></strong><small>Lada Vesta · скидка 25 000 ₽ · действительно 10 дней</small><button className="secondary">Открыть сравнение версий</button></div></section></div></>
}

function DemoReservations() {
  const [confirmed, setConfirmed] = useState(false)
  return <><PageHeader eyebrow="Продажи" title="Бронирования" description="Сроки брони, предоплата и связь с предложением." /><section className="panel"><div className="reservation-list"><button><span><strong>Lada Vesta · MSK-0241</strong><small>Клиент Демо №1042 · предложение №D-0184</small></span><span className={`status ${confirmed ? 'success' : 'warning'}`}><i />{confirmed ? 'Предоплата получена' : 'Ожидает предоплату'}</span><strong className="countdown">22 ч 14 мин</strong></button></div><div className="form-actions"><span><strong>Демонстрационное действие</strong><small>Состояние изменится только в памяти этой вкладки</small></span><button className="primary" onClick={() => setConfirmed((value) => !value)}>{confirmed ? 'Вернуть ожидание' : 'Подтвердить предоплату'}</button></div></section></>
}

function DemoDeals() {
  return <><PageHeader eyebrow="Сделка" title="Сделки" description="Документы, оплата и готовность автомобиля к выдаче." /><div className="demo-detail-grid"><section className="panel"><p className="eyebrow">Сделка №DL-0098</p><h2>Lada Vesta · Клиент Демо №1042</h2><div className="snapshot-card"><span>К оплате</span><strong><Money value={1_495_000} /></strong><small>Предоплата 50 000 ₽ · остаток 1 445 000 ₽</small></div></section><section className="panel"><p className="eyebrow">Прогресс</p><h2>Подготовка к выдаче</h2><div className="demo-checklist"><span className="done">✓ Договор сформирован</span><span className="done">✓ Предоплата зафиксирована</span><span>○ Полная оплата</span><span>○ Акт выдачи</span></div></section></div></>
}

function DemoFinance() {
  return <><PageHeader eyebrow="Аналитика" title="Фактическая экономика" description="Синтетическая выручка, затраты и прибыль по завершённым сделкам." /><div className="dashboard-metrics"><DemoMetric icon="finance" label="Чистая выручка" value="11,4 млн ₽" /><DemoMetric icon="deal" label="Фактическая прибыль" value="2,25 млн ₽" /><DemoMetric icon="car" label="Продано" value="9" /><DemoMetric icon="dashboard" label="Средняя маржа" value="19,7%" /></div><section className="registry"><div className="registry-heading"><div><p className="eyebrow">Последние 90 дней</p><h2>Экономика по автомобилям</h2></div></div><div className="table-wrap"><table><thead><tr><th>Автомобиль</th><th className="numeric">Выручка</th><th className="numeric">Затраты</th><th className="numeric">Прибыль</th><th>Маржа</th></tr></thead><tbody><tr><td><strong>Lada Vesta</strong><small>MSK-0217 · завершённая сделка</small></td><td className="numeric">1 490 000 ₽</td><td className="numeric">1 185 000 ₽</td><td className="numeric">305 000 ₽</td><td><span className="status success"><i />20,5%</span></td></tr><tr><td><strong>Toyota Camry</strong><small>MSK-0209 · завершённая сделка</small></td><td className="numeric">3 420 000 ₽</td><td className="numeric">2 890 000 ₽</td><td className="numeric">530 000 ₽</td><td><span className="status success"><i />15,5%</span></td></tr></tbody></table></div></section></>
}
