import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { api, ApiError, getSession } from './api'
import type { Branch, Vehicle } from './App'

export type Customer = {
  id: string; createdInBranchId: string; type: string; name: string; normalizedPhone?: string
  normalizedEmail?: string; preferredChannel: string; consentGiven: boolean; marketingConsent: boolean
  isMerged: boolean; mergedIntoCustomerId?: string; version: number; createdAt: string
}
export type LeadActivity = { id: string; type: string; direction: string; result?: string; summary: string; isClosed: boolean; createdAt: string }
export type Lead = {
  id: string; branchId: string; customerId: string; customerName: string; vehicleId?: string
  searchCriteria?: string; source: string; assignedManagerUserId?: string; assignedManagerName?: string
  status: string; createdAt: string; firstResponseDueAt: string; firstResponseAt?: string; slaBreached: boolean
  lostReason?: string; nextAction?: string; nextActionDueAt?: string; version: number; activities: LeadActivity[]
  history: { commandId: string; fromStatus?: string; toStatus: string; operation: string; occurredAt: string }[]
}
type Manager = { id: string; displayName: string }
type CustomerCreate = { customer: Customer; possibleDuplicates: Customer[] }
type MergePreview = { source: Customer; target: Customer; leadsToMove: number; warning: string }

export function CrmWorkspace() {
  const client = useQueryClient()
  const permissions = getSession()?.permissions ?? []
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('Active')
  const [branchFilter, setBranchFilter] = useState('All')
  const [managerFilter, setManagerFilter] = useState('All')
  const [selectedCustomer, setSelectedCustomer] = useState<Customer | null>(null)
  const [selectedLead, setSelectedLead] = useState<Lead | null>(null)
  const [duplicates, setDuplicates] = useState<Customer[]>([])
  const [customerDraft, setCustomerDraft] = useState({ branchId: '', name: '', phone: '', email: '', consentGiven: false, marketingConsent: false, consentSource: 'Личная заявка' })
  const [leadDraft, setLeadDraft] = useState({ branchId: '', vehicleId: '', searchCriteria: '', source: 'Входящий звонок' })
  const [summary, setSummary] = useState('')
  const [nextAction, setNextAction] = useState('Перезвонить клиенту')
  const [managerId, setManagerId] = useState('')
  const [closeReason, setCloseReason] = useState('')
  const [mergePreview, setMergePreview] = useState<MergePreview | null>(null)
  const [mergeReason, setMergeReason] = useState('Подтверждённый дубль контактных данных')

  const customers = useQuery({ queryKey: ['crm-customers', query], queryFn: () => api<Customer[]>(`/api/crm/customers?query=${encodeURIComponent(query)}`) })
  const leads = useQuery({ queryKey: ['crm-leads'], queryFn: () => api<Lead[]>('/api/crm/leads') })
  const branches = useQuery({ queryKey: ['branches'], queryFn: () => api<Branch[]>('/api/branches') })
  const vehicles = useQuery({ queryKey: ['vehicles'], queryFn: () => api<Vehicle[]>('/api/vehicles') })
  const managers = useQuery({ queryKey: ['crm-managers', selectedLead?.branchId], queryFn: () => api<Manager[]>(`/api/crm/managers?branchId=${selectedLead!.branchId}`), enabled: Boolean(selectedLead && permissions.includes('crm.leads.assign')) })
  const visibleLeads = useMemo(() => (leads.data ?? []).filter((lead) =>
    (branchFilter === 'All' || lead.branchId === branchFilter)
    && (managerFilter === 'All' || lead.assignedManagerUserId === managerFilter)
    && (status === 'All' || (status === 'Overdue' ? lead.slaBreached : status === 'Active' ? !['Lost', 'Spam', 'Duplicate'].includes(lead.status) : lead.status === status))), [branchFilter, leads.data, managerFilter, status])
  const leadManagers = useMemo(() => Array.from(new Map((leads.data ?? []).filter((x) => x.assignedManagerUserId).map((x) => [x.assignedManagerUserId!, { id: x.assignedManagerUserId!, name: x.assignedManagerName ?? x.assignedManagerUserId! }])).values()), [leads.data])
  const refreshLead = (lead: Lead) => { setSelectedLead(lead); client.invalidateQueries({ queryKey: ['crm-leads'] }) }
  const firstBranch = branches.data?.[0]?.id ?? ''

  const createCustomer = useMutation({
    mutationFn: () => api<CustomerCreate>('/api/crm/customers', { method: 'POST', body: { ...customerDraft, branchId: customerDraft.branchId || firstBranch, type: 'Individual', preferredChannel: customerDraft.email ? 'Email' : 'Phone', consentAt: customerDraft.consentGiven || customerDraft.marketingConsent ? new Date().toISOString() : null, consentSource: customerDraft.consentGiven || customerDraft.marketingConsent ? customerDraft.consentSource : null } }),
    onSuccess: (result) => { setSelectedCustomer(result.customer); setDuplicates(result.possibleDuplicates); client.invalidateQueries({ queryKey: ['crm-customers'] }) },
  })
  const createLead = useMutation({
    mutationFn: () => api<Lead>('/api/crm/leads', { method: 'POST', body: { ...leadDraft, branchId: leadDraft.branchId || selectedCustomer?.createdInBranchId || firstBranch, customerId: selectedCustomer!.id, vehicleId: leadDraft.vehicleId || null, searchCriteria: leadDraft.vehicleId ? null : leadDraft.searchCriteria } }),
    onSuccess: refreshLead,
  })
  const command = useMutation({
    mutationFn: ({ path, body }: { path: string; body: object }) => api<Lead>(path, { method: 'POST', body }),
    onSuccess: refreshLead,
  })
  const merge = useMutation({
    mutationFn: () => api<Customer>(`/api/crm/customers/${mergePreview!.source.id}/merge`, { method: 'POST', body: { commandId: crypto.randomUUID(), targetCustomerId: mergePreview!.target.id, reason: mergeReason, expectedSourceVersion: mergePreview!.source.version } }),
    onSuccess: (customer) => { setSelectedCustomer(customer); setDuplicates([]); setMergePreview(null); client.invalidateQueries({ queryKey: ['crm-customers'] }); client.invalidateQueries({ queryKey: ['crm-leads'] }) },
  })
  const previewMerge = useMutation({ mutationFn: (target: Customer) => api<MergePreview>(`/api/crm/customers/${selectedCustomer!.id}/merge-preview/${target.id}`), onSuccess: setMergePreview })
  const error = customers.error ?? leads.error ?? branches.error ?? vehicles.error ?? createCustomer.error ?? createLead.error ?? command.error ?? previewMerge.error ?? merge.error

  const run = (action: string, body: object) => selectedLead && command.mutate({ path: `/api/crm/leads/${selectedLead.id}/${action}`, body })
  const dueTomorrow = () => new Date(Date.now() + 86_400_000).toISOString()

  return <>
    <div className="page-heading"><div><p className="eyebrow">Продажи</p><h1>Клиенты и обращения</h1><p className="muted">Единая очередь обращений, безопасное объединение дублей и контроль срока первого содержательного контакта.</p></div><div className="metric"><span>Срок ответа нарушен</span><strong>{(leads.data ?? []).filter((x) => x.slaBreached).length}</strong><small>обращений требуют внимания</small></div></div>
    {error && <div className="error-banner workspace-error" role="alert">{errorText(error)}</div>}
    <div className="crm-grid">
      <section className="panel"><div className="panel-title"><span className="step">07</span><div><h2>Реестр клиентов</h2><p>Контакты нормализуются сервером; совпадения не объединяются автоматически.</p></div></div>
        <label className="full">Поиск по имени, телефону или email<input aria-label="Поиск клиентов" value={query} onChange={(e) => setQuery(e.target.value)} /></label>
        <div className="customer-list">{customers.isLoading ? <p>Загрузка…</p> : customers.data?.length ? customers.data.map((customer) => <button key={customer.id} className={`customer-row ${selectedCustomer?.id === customer.id ? 'selected' : ''}`} onClick={() => setSelectedCustomer(customer)}><span><strong>{customer.name}</strong><small>{customer.normalizedPhone ?? customer.normalizedEmail}</small></span><span className={`status ${customer.isMerged ? 'draft' : 'success'}`}><i />{customer.isMerged ? 'Объединён' : customerTypeLabel(customer.type)}</span></button>) : <div className="empty-state compact"><strong>Клиенты не найдены</strong></div>}</div>
        {permissions.includes('crm.customers.edit') && <div className="work-form crm-form"><label>Филиал<select aria-label="Филиал клиента" value={customerDraft.branchId || firstBranch} onChange={(e) => setCustomerDraft({ ...customerDraft, branchId: e.target.value })}>{branches.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label>Имя<input aria-label="Имя клиента" value={customerDraft.name} onChange={(e) => setCustomerDraft({ ...customerDraft, name: e.target.value })} /></label><label>Телефон<input aria-label="Телефон клиента" value={customerDraft.phone} onChange={(e) => setCustomerDraft({ ...customerDraft, phone: e.target.value })} /></label><label>Email<input aria-label="Email клиента" value={customerDraft.email} onChange={(e) => setCustomerDraft({ ...customerDraft, email: e.target.value })} /></label><label className="check-row"><input type="checkbox" checked={customerDraft.consentGiven} onChange={(e) => setCustomerDraft({ ...customerDraft, consentGiven: e.target.checked })} />Согласие на контакт</label><label className="check-row"><input type="checkbox" checked={customerDraft.marketingConsent} onChange={(e) => setCustomerDraft({ ...customerDraft, marketingConsent: e.target.checked })} />Маркетинговое согласие</label><button className="secondary" disabled={!customerDraft.name || (!customerDraft.phone && !customerDraft.email) || createCustomer.isPending} onClick={() => createCustomer.mutate()}>Создать клиента</button></div>}
        {duplicates.length > 0 && <div className="critical-banner duplicate-warning"><strong>Возможный дубль</strong><span>Объединение требует отдельного preview, решения и фиксируемой причины.</span>{duplicates.map((x) => <button key={x.id} className="secondary" disabled={!permissions.includes('crm.customers.merge')} onClick={() => previewMerge.mutate(x)}>Предпросмотр: {x.name}</button>)}</div>}
        {mergePreview && <div className="warning-banner"><strong>Будет перемещено обращений: {mergePreview.leadsToMove}</strong><span>{mergePreview.warning}</span><label>Причина объединения<input aria-label="Причина объединения" value={mergeReason} onChange={(e) => setMergeReason(e.target.value)} /></label><button className="danger-button" disabled={!mergeReason || merge.isPending} onClick={() => merge.mutate()}>Подтвердить объединение с {mergePreview.target.name}</button></div>}
      </section>

      <section className="panel"><div className="panel-title"><span className="step">08</span><div><h2>Очередь обращений</h2><p>Назначение и каждое изменение статуса выполняются отдельной командой.</p></div></div>
        <div className="filter-row"><label>Филиал<select aria-label="Фильтр филиала" value={branchFilter} onChange={(e) => setBranchFilter(e.target.value)}><option value="All">Все</option>{branches.data?.map((x) => <option value={x.id} key={x.id}>{x.name}</option>)}</select></label><label>Менеджер<select aria-label="Фильтр менеджера" value={managerFilter} onChange={(e) => setManagerFilter(e.target.value)}><option value="All">Все</option>{leadManagers.map((x) => <option value={x.id} key={x.id}>{x.name}</option>)}</select></label><label>Статус<select aria-label="Фильтр лидов" value={status} onChange={(e) => setStatus(e.target.value)}><option value="Active">Активные</option><option value="Overdue">Просроченные</option><option value="New">Новые</option><option value="Assigned">Назначенные</option><option value="FirstContact">Первый контакт</option><option value="Qualified">Квалифицированные</option><option value="Lost">Потерянные</option><option value="All">Все</option></select></label></div>
        {selectedCustomer && permissions.includes('crm.leads.create') && <div className="work-form crm-form"><p className="wide"><strong>Новое обращение: {selectedCustomer.name}</strong></p><label>Филиал<select aria-label="Филиал лида" value={leadDraft.branchId || selectedCustomer.createdInBranchId || firstBranch} onChange={(e) => setLeadDraft({ ...leadDraft, branchId: e.target.value })}>{branches.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label>Автомобиль<select aria-label="Автомобиль лида" value={leadDraft.vehicleId} onChange={(e) => setLeadDraft({ ...leadDraft, vehicleId: e.target.value })}><option value="">По параметрам</option>{vehicles.data?.map((x) => <option key={x.id} value={x.id}>{x.make} {x.model}</option>)}</select></label><label>Источник<input aria-label="Источник лида" value={leadDraft.source} onChange={(e) => setLeadDraft({ ...leadDraft, source: e.target.value })} /></label><label>Параметры поиска<input aria-label="Параметры поиска" disabled={Boolean(leadDraft.vehicleId)} value={leadDraft.searchCriteria} onChange={(e) => setLeadDraft({ ...leadDraft, searchCriteria: e.target.value })} /></label><button className="secondary" disabled={!leadDraft.source || (!leadDraft.vehicleId && !leadDraft.searchCriteria) || createLead.isPending} onClick={() => createLead.mutate()}>Создать обращение</button></div>}
        {!selectedLead ? <div className="lead-list">{leads.isLoading ? <p>Загрузка…</p> : visibleLeads.length ? visibleLeads.map((lead) => <button className={`lead-row ${lead.slaBreached ? 'overdue' : ''}`} key={lead.id} onClick={() => setSelectedLead(lead)}><span><strong>{lead.customerName}</strong><small>{lead.vehicleId ? 'Выбран автомобиль' : lead.searchCriteria} · {lead.source}</small></span><span><b>{leadStatusLabel(lead.status)}</b><small>{lead.slaBreached ? 'SLA просрочен' : `до ${dateTime(lead.firstResponseDueAt)}`}</small></span></button>) : <div className="empty-state compact"><strong>В выбранном фильтре обращений нет</strong></div>}</div> : <LeadCard lead={selectedLead} managers={managers.data ?? []} managerId={managerId} setManagerId={setManagerId} summary={summary} setSummary={setSummary} nextAction={nextAction} setNextAction={setNextAction} closeReason={closeReason} setCloseReason={setCloseReason} pending={command.isPending} onBack={() => setSelectedLead(null)} onRoundRobin={() => run('assign-round-robin', { commandId: crypto.randomUUID(), expectedVersion: selectedLead.version })} onAssign={() => run('assign', { commandId: crypto.randomUUID(), managerUserId: managerId, expectedVersion: selectedLead.version })} onContact={() => run('activities', { commandId: crypto.randomUUID(), type: 'Call', direction: 'Outbound', result: 'Answered', summary, meaningfulContact: true, nextAction, nextActionDueAt: dueTomorrow(), expectedVersion: selectedLead.version })} onQualify={() => run('qualify', { commandId: crypto.randomUUID(), nextAction, nextActionDueAt: dueTomorrow(), expectedVersion: selectedLead.version })} onClose={() => run('close', { commandId: crypto.randomUUID(), status: 'Lost', reason: closeReason, expectedVersion: selectedLead.version })} />}
      </section>
    </div>
  </>
}

function LeadCard(props: { lead: Lead; managers: Manager[]; managerId: string; setManagerId: (x: string) => void; summary: string; setSummary: (x: string) => void; nextAction: string; setNextAction: (x: string) => void; closeReason: string; setCloseReason: (x: string) => void; pending: boolean; onBack: () => void; onRoundRobin: () => void; onAssign: () => void; onContact: () => void; onQualify: () => void; onClose: () => void }) {
  const { lead } = props
  return <div className="lead-card"><button className="link-button" onClick={props.onBack}>← К обращениям</button><div className="work-card-head"><div><span className={`status ${lead.slaBreached ? 'danger' : 'draft'}`}><i />{leadStatusLabel(lead.status)}{lead.slaBreached ? ' · SLA просрочен' : ''}</span><h3>{lead.customerName}</h3><p>{lead.searchCriteria}</p></div><div><small>Менеджер</small><strong>{lead.assignedManagerName ?? 'Не назначен'}</strong></div></div>
    {lead.status === 'New' && <div className="work-actions"><button className="secondary" disabled={props.pending} onClick={props.onRoundRobin}>Назначить автоматически</button><label>Вручную<select aria-label="Менеджер" value={props.managerId} onChange={(e) => props.setManagerId(e.target.value)}><option value="">Выберите</option>{props.managers.map((x) => <option key={x.id} value={x.id}>{x.displayName}</option>)}</select></label><button className="secondary" disabled={!props.managerId || props.pending} onClick={props.onAssign}>Назначить</button></div>}
    {lead.status === 'Assigned' && <div className="work-form"><label className="wide">Краткий итог первого контакта<textarea aria-label="Итог первого контакта" value={props.summary} onChange={(e) => props.setSummary(e.target.value)} /></label><label className="wide">Следующее действие<input aria-label="Следующее действие" value={props.nextAction} onChange={(e) => props.setNextAction(e.target.value)} /></label><button className="primary" disabled={!props.summary || !props.nextAction || props.pending} onClick={props.onContact}>Зафиксировать первый контакт</button></div>}
    {lead.status === 'FirstContact' && <div className="work-actions"><label>Следующее действие<input aria-label="Действие после квалификации" value={props.nextAction} onChange={(e) => props.setNextAction(e.target.value)} /></label><button className="primary" disabled={!props.nextAction || props.pending} onClick={props.onQualify}>Квалифицировать</button></div>}
    {!['Lost', 'Spam', 'Duplicate'].includes(lead.status) && <div className="work-actions"><label>Причина потери<input aria-label="Причина потери" value={props.closeReason} onChange={(e) => props.setCloseReason(e.target.value)} /></label><button className="secondary" disabled={!props.closeReason || props.pending} onClick={props.onClose}>Закрыть как потерянное</button></div>}
    <div className="timeline"><h3>Таймлайн</h3>{lead.activities.length ? lead.activities.map((x) => <article key={x.id}><span>{dateTime(x.createdAt)}</span><strong>{activityLabel(x.type)} · {directionLabel(x.direction)}</strong><p>{x.summary}</p></article>) : <p className="muted">Контактов пока нет.</p>}</div>
  </div>
}

function dateTime(value: string) { return new Date(value).toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'short' }) }
function leadStatusLabel(status: string) { return ({ New: 'Новое', Assigned: 'Назначено', FirstContact: 'Первый контакт', Qualified: 'Квалифицировано', Lost: 'Потеряно', Spam: 'Спам', Duplicate: 'Дубль' } as Record<string, string>)[status] ?? status }
function customerTypeLabel(type: string) { return ({ Individual: 'Физлицо', Legal: 'Юрлицо' } as Record<string, string>)[type] ?? type }
function activityLabel(type: string) { return ({ Call: 'Звонок', Email: 'Письмо', Message: 'Сообщение', Meeting: 'Встреча' } as Record<string, string>)[type] ?? type }
function directionLabel(direction: string) { return ({ Inbound: 'Входящий', Outbound: 'Исходящий' } as Record<string, string>)[direction] ?? direction }
function errorText(error: unknown) { return error instanceof ApiError ? error.message : 'Не удалось выполнить CRM-операцию.' }
