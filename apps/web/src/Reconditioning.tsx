import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { api, ApiError, getSession } from './api'

type PlanStatus = 'Draft' | 'Submitted' | 'ChangesRequested' | 'Approved' | 'Rejected' | 'Cancelled'
export type ReconditioningQueueVehicle = {
  vehicleId: string; branchId: string; branchName: string; vin: string; make: string; model: string; year: number
  stockNumber?: string; inspectionId: string; inspectionCompletedAt: string; mandatoryDefectCount: number; hasActivePlan: boolean
  latestPlanId?: string; latestPlanStatus?: PlanStatus; latestPlanRevision?: number
}
export type ReconditioningBudget = { currency: string; laborAmount: number; partsAmount: number; totalAmount: number }
export type ReconditioningWork = {
  id: string; sourceDefectId: string; sourceDefectTitle: string; sourceDefectDescription: string
  sourceDefectSeverity: string; title: string; description: string; category: string; priority: string
  isMandatory: boolean; executorType: string; executorName: string; estimatedLaborAmount: number
  estimatedPartsAmount: number; currency: string; estimatedDurationDays: number; comment?: string
}
export type ReconditioningPlanSummary = {
  id: string; vehicleId: string; branchId: string; branchName: string; vin: string; make: string; model: string
  status: PlanStatus; revision: number; sourceInspectionId: string; revisesPlanId?: string; createdByName: string
  workCount: number; budgets: ReconditioningBudget[]; version: number; updatedAt: string
}
export type ReconditioningPlan = ReconditioningPlanSummary & {
  stockNumber?: string; createdByUserId: string; submittedAt?: string; decidedAt?: string; cancelledAt?: string
  createdAt: string; works: ReconditioningWork[]
  omissions: { id: string; sourceDefectId: string; sourceDefectTitle: string; reason: string; decidedByName: string; decidedAt: string }[]
  decisions: { id: string; type: string; actorName: string; reason?: string; approvedLimitAmount?: number; currency?: string; decidedAt: string }[]
  approvedBudgetSnapshots: { id: string; laborAmount: number; partsAmount: number; plannedTotalAmount: number; approvedLimitAmount: number; currency: string; approvedByName: string; approvedAt: string }[]
  history: { id: string; fromStatus?: string; toStatus: string; actorName: string; reason?: string; occurredAt: string }[]
}

type Command = { path: string; method?: string; body: unknown }
const categories = ['Body', 'Mechanical', 'Interior', 'Electrical', 'WheelsAndTires', 'Detailing', 'Documents', 'Other']
const priorities = ['Low', 'Normal', 'High', 'Critical']

function problem(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 409) return 'Конфликт версий: план изменён в другом окне. Обновите данные.'
    return error.message
  }
  return 'Операция не выполнена. Повторите попытку.'
}

function money(value: number, currency: string) {
  return new Intl.NumberFormat('ru-RU', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value)
}

export function ReconditioningWorkspace() {
  const queryClient = useQueryClient()
  const [activeId, setActiveId] = useState<string | null>(null)
  const [decisionReason, setDecisionReason] = useState('')
  const [approvedLimit, setApprovedLimit] = useState('')
  const permissions = getSession()?.permissions ?? []
  const canApprove = permissions.includes('reconditioning.approve')
  const required = useQuery({ queryKey: ['reconditioning-required'], queryFn: () => api<ReconditioningQueueVehicle[]>('/api/reconditioning-plans/required-vehicles') })
  const approvals = useQuery({ queryKey: ['reconditioning-approvals'], queryFn: () => api<ReconditioningPlanSummary[]>('/api/reconditioning-plans/approvals'), retry: false, enabled: canApprove })
  const planQuery = useQuery({ queryKey: ['reconditioning-plan', activeId], queryFn: () => api<ReconditioningPlan>(`/api/reconditioning-plans/${activeId}`), enabled: Boolean(activeId), retry: false })
  const plan = planQuery.data
  const revisions = useQuery({ queryKey: ['vehicle-reconditioning-plans', plan?.vehicleId], queryFn: () => api<ReconditioningPlanSummary[]>(`/api/vehicles/${plan!.vehicleId}/reconditioning-plans`), enabled: Boolean(plan?.vehicleId) })
  const previous = useQuery({ queryKey: ['reconditioning-plan', plan?.revisesPlanId], queryFn: () => api<ReconditioningPlan>(`/api/reconditioning-plans/${plan!.revisesPlanId}`), enabled: Boolean(plan?.revisesPlanId) })

  const refresh = (next: ReconditioningPlan) => {
    setActiveId(next.id)
    queryClient.setQueryData(['reconditioning-plan', next.id], next)
    queryClient.invalidateQueries({ queryKey: ['reconditioning-required'] })
    queryClient.invalidateQueries({ queryKey: ['reconditioning-approvals'] })
    queryClient.invalidateQueries({ queryKey: ['vehicle-reconditioning-plans', next.vehicleId] })
  }
  const command = useMutation({ mutationFn: ({ path, method, body }: Command) => api<ReconditioningPlan>(path, { method: method ?? 'POST', body }), onSuccess: refresh })
  const create = useMutation({ mutationFn: (vehicle: ReconditioningQueueVehicle) => api<ReconditioningPlan>(`/api/vehicles/${vehicle.vehicleId}/reconditioning-plans`, { method: 'POST', body: { inspectionId: vehicle.inspectionId } }), onSuccess: refresh })

  if (activeId) {
    if (planQuery.isLoading) return <div className="inspection-loading">Загружаем план подготовки…</div>
    if (planQuery.error || !plan) return <div className="error-banner" role="alert">{problem(planQuery.error)} <button className="inline-action" onClick={() => setActiveId(null)}>Вернуться</button></div>
    const editable = plan.status === 'Draft' || plan.status === 'ChangesRequested'
    const submitted = plan.status === 'Submitted'
    const snapshot = plan.approvedBudgetSnapshots.at(-1)
    return <div className="reconditioning-workspace">
      <div className="plan-toolbar">
        <button className="link-button dark" onClick={() => setActiveId(null)}>← Очереди</button>
        <span className={`plan-status ${plan.status.toLowerCase()}`}>{statusLabel(plan.status)}</span>
        <span>Ревизия {plan.revision}</span><span className="mono">{plan.vin}</span>
      </div>
      <div className="page-heading plan-heading"><div><p className="eyebrow">План предпродажной подготовки</p><h1>{plan.make} {plan.model}</h1><p className="muted">{plan.branchName} · Автор: {plan.createdByName} · Осмотр-основание</p><details><summary>Техническая информация</summary><span className="mono">{plan.sourceInspectionId}</span></details></div><BudgetCards budgets={plan.budgets} approved={snapshot?.approvedLimitAmount} /></div>
      {plan.budgets.length > 1 && <div className="critical-banner" role="alert"><strong>В плане несколько валют.</strong><span>Суммы показаны раздельно. Приведите работы к одной валюте перед отправкой.</span></div>}
      {plan.omissions.length > 0 && <div className="warning-banner" role="alert"><strong>Обязательные дефекты исключены из состава</strong>{plan.omissions.map((x) => <span key={x.id}>{x.sourceDefectTitle}: {x.reason}</span>)}</div>}
      {command.error && <div className="error-banner" role="alert">{problem(command.error)}</div>}

      {previous.data && <RevisionComparison current={plan} previous={previous.data} />}
      <div className="plan-layout">
        <section className="panel plan-editor">
          <div className="panel-title"><span className="step">03</span><div><h2>Состав работ</h2><p>{editable ? 'Стоимость и состав доступны для редактирования.' : 'Зафиксированный состав доступен только для чтения.'}</p></div></div>
          {plan.works.length === 0 ? <div className="empty-state"><strong>Работ пока нет</strong><p>Пустой план нельзя отправить на согласование.</p></div> : plan.works.map((work) => <WorkEditor key={`${work.id}-${plan.version}`} work={work} editable={editable} busy={command.isPending} onSave={(body) => command.mutate({ path: `/api/reconditioning-plans/${plan.id}/works/${work.id}`, method: 'PUT', body: { ...body, expectedVersion: plan.version } })} onRemove={(omissionReason) => command.mutate({ path: `/api/reconditioning-plans/${plan.id}/works/${work.id}/remove`, body: { omissionReason, expectedVersion: plan.version } })} />)}
          {editable && plan.works.length > 0 && <AddWork plan={plan} busy={command.isPending} onAdd={(body) => command.mutate({ path: `/api/reconditioning-plans/${plan.id}/works`, body: { ...body, expectedVersion: plan.version } })} />}
        </section>
        <aside className="panel approval-panel">
          <div className="panel-title"><span className="step">04</span><div><h2>Согласование</h2><p>Отдельные серверные команды и полная история.</p></div></div>
          {editable && <button className="primary" disabled={command.isPending || plan.works.length === 0 || plan.budgets.length !== 1} onClick={() => command.mutate({ path: `/api/reconditioning-plans/${plan.id}/submit`, body: { expectedVersion: plan.version } })}>Отправить руководителю</button>}
          {submitted && canApprove && <div className="decision-form">
            <label>Лимит бюджета<input aria-label="Одобренный лимит" type="number" min="0" value={approvedLimit} placeholder={String(plan.budgets[0]?.totalAmount ?? 0)} onChange={(event) => setApprovedLimit(event.target.value)} /></label>
            <label>Комментарий или причина<textarea aria-label="Причина решения" value={decisionReason} onChange={(event) => setDecisionReason(event.target.value)} /></label>
            <button className="primary" onClick={() => command.mutate({ path: `/api/reconditioning-plans/${plan.id}/approve`, body: { decisionId: crypto.randomUUID(), approvedLimitAmount: approvedLimit ? Number(approvedLimit) : undefined, currency: plan.budgets[0]?.currency, comment: decisionReason || undefined, expectedVersion: plan.version } })}>Утвердить бюджет</button>
            <button className="secondary" disabled={!decisionReason.trim()} onClick={() => command.mutate({ path: `/api/reconditioning-plans/${plan.id}/request-changes`, body: { decisionId: crypto.randomUUID(), reason: decisionReason, expectedVersion: plan.version } })}>Вернуть на доработку</button>
            <button className="danger-button" disabled={!decisionReason.trim()} onClick={() => command.mutate({ path: `/api/reconditioning-plans/${plan.id}/reject`, body: { decisionId: crypto.randomUUID(), reason: decisionReason, expectedVersion: plan.version } })}>Отклонить</button>
          </div>}
          {submitted && !canApprove && <div className="info-banner">План находится в очереди руководителя. Редактирование временно заблокировано.</div>}
          {plan.status === 'Approved' && <div className="success-banner" role="status"><span>✓</span><div><strong>Бюджет утверждён</strong><small>{snapshot ? `${money(snapshot.approvedLimitAmount, snapshot.currency)} · ${snapshot.approvedByName}` : 'Снимок бюджета сохранён'}</small></div></div>}
          {plan.status === 'Approved' && !canApprove && <button className="secondary" onClick={() => command.mutate({ path: `/api/reconditioning-plans/${plan.id}/revisions`, body: { expectedVersion: plan.version } })}>Создать новую ревизию</button>}
          {(plan.status === 'Rejected' || plan.status === 'Cancelled') && !canApprove && <button className="secondary" onClick={() => command.mutate({ path: `/api/vehicles/${plan.vehicleId}/reconditioning-plans`, body: { inspectionId: plan.sourceInspectionId } })}>Создать новый черновик</button>}
          <History plan={plan} />
          {revisions.data && revisions.data.length > 0 && <div className="revision-list"><h3>Ревизии</h3>{revisions.data.map((item) => <button key={item.id} className={item.id === plan.id ? 'active' : ''} onClick={() => setActiveId(item.id)}><span>Ревизия {item.revision}</span><strong>{statusLabel(item.status)}</strong></button>)}</div>}
        </aside>
      </div>
    </div>
  }

  return <div className="reconditioning-workspace">
    <div className="page-heading"><div><p className="eyebrow">Предпродажная подготовка</p><h1>Планы и бюджеты</h1><p className="muted">Перенесите обязательные дефекты из осмотра, оцените работы и получите решение руководителя.</p></div><div className="metric"><span>Требуют плана</span><strong>{required.data?.filter((x) => !x.hasActivePlan).length ?? 0}</strong><small>автомобилей</small></div></div>
    {(required.error || approvals.error) && <div className="error-banner" role="alert">{problem(required.error ?? approvals.error)}</div>}
    <section className="registry queue-section"><div><p className="eyebrow">Очередь подготовки</p><h2>Автомобили после осмотра</h2></div>
      {required.isLoading ? <p>Загрузка…</p> : required.data?.length ? <div className="queue-grid">{required.data.map((vehicle) => <article className="queue-card" key={vehicle.vehicleId}><span className="status draft"><i />{vehicle.mandatoryDefectCount} обязательных дефекта</span><h3>{vehicle.make} {vehicle.model}</h3><p className="mono">{vehicle.vin}</p><dl><div><dt>Филиал</dt><dd>{vehicle.branchName}</dd></div><div><dt>Осмотр</dt><dd>{new Date(vehicle.inspectionCompletedAt).toLocaleDateString('ru-RU')}</dd></div>{vehicle.latestPlanStatus && <div><dt>Последний план</dt><dd>{statusLabel(vehicle.latestPlanStatus)} · рев. {vehicle.latestPlanRevision}</dd></div>}</dl><button className="primary" disabled={create.isPending} onClick={() => vehicle.latestPlanId ? setActiveId(vehicle.latestPlanId) : create.mutate(vehicle)}>{vehicle.latestPlanId ? 'Открыть последний план' : 'Создать план'}</button></article>)}</div> : <div className="empty-state"><strong>Очередь пуста</strong><p>Нет автомобилей, которым требуется предпродажная подготовка.</p></div>}
    </section>
    {canApprove && <section className="registry approvals-queue"><div><p className="eyebrow">Руководитель</p><h2>Очередь согласований</h2></div>{approvals.isLoading ? <p>Загрузка…</p> : approvals.data?.length ? <div className="history-list">{approvals.data.map((item) => <button key={item.id} onClick={() => setActiveId(item.id)}><strong>{item.make} {item.model} · {budgetTotal(item.budgets)}</strong><span>{item.vin} · {item.createdByName} · ревизия {item.revision}</span></button>)}</div> : <div className="empty-state"><strong>Всё согласовано</strong><p>Новых планов в очереди нет.</p></div>}</section>}
  </div>
}

function WorkEditor({ work, editable, busy, onSave, onRemove }: { work: ReconditioningWork; editable: boolean; busy: boolean; onSave: (body: object) => void; onRemove: (reason?: string) => void }) {
  const [draft, setDraft] = useState(work)
  const [removing, setRemoving] = useState(false)
  const [reason, setReason] = useState('')
  useEffect(() => setDraft(work), [work])
  const set = (key: keyof ReconditioningWork, value: string | number | boolean) => setDraft((current) => ({ ...current, [key]: value }))
  return <article className={`work-card ${work.isMandatory ? 'mandatory' : ''}`}>
    <div className="work-source"><span>{work.isMandatory ? 'Обязательный дефект' : 'Исходный дефект'}</span><strong>{work.sourceDefectTitle}</strong><small>{work.sourceDefectSeverity}</small></div>
    <div className="work-fields">
      <label className="wide">Название<input aria-label={`Название работы: ${work.sourceDefectTitle}`} disabled={!editable} value={draft.title} onChange={(e) => set('title', e.target.value)} /></label>
      <label className="wide">Описание<textarea aria-label={`Описание работы: ${work.sourceDefectTitle}`} disabled={!editable} value={draft.description} onChange={(e) => set('description', e.target.value)} /></label>
      <label>Категория<select aria-label={`Категория работы: ${work.sourceDefectTitle}`} disabled={!editable} value={draft.category} onChange={(e) => set('category', e.target.value)}>{categories.map((x) => <option key={x} value={x}>{categoryLabel(x)}</option>)}</select></label>
      <label>Приоритет<select aria-label={`Приоритет работы: ${work.sourceDefectTitle}`} disabled={!editable} value={draft.priority} onChange={(e) => set('priority', e.target.value)}>{priorities.map((x) => <option key={x} value={x}>{priorityLabel(x)}</option>)}</select></label>
      <label>Исполнитель<select aria-label={`Тип исполнителя: ${work.sourceDefectTitle}`} disabled={!editable} value={draft.executorType} onChange={(e) => set('executorType', e.target.value)}><option value="Internal">Внутренний</option><option value="External">Внешний</option></select></label>
      <label>Название исполнителя<input aria-label={`Исполнитель: ${work.sourceDefectTitle}`} disabled={!editable} value={draft.executorName} onChange={(e) => set('executorName', e.target.value)} /></label>
      <label>Работа<input aria-label={`Стоимость работы: ${work.sourceDefectTitle}`} type="number" min="0" disabled={!editable} value={draft.estimatedLaborAmount} onChange={(e) => set('estimatedLaborAmount', Number(e.target.value))} /></label>
      <label>Запчасти<input aria-label={`Стоимость запчастей: ${work.sourceDefectTitle}`} type="number" min="0" disabled={!editable} value={draft.estimatedPartsAmount} onChange={(e) => set('estimatedPartsAmount', Number(e.target.value))} /></label>
      <label>Валюта<select aria-label={`Валюта работы: ${work.sourceDefectTitle}`} disabled={!editable} value={draft.currency} onChange={(e) => set('currency', e.target.value)}><option>RUB</option><option>USD</option><option>EUR</option><option>CNY</option></select></label>
      <label>Срок, дней<input aria-label={`Срок: ${work.sourceDefectTitle}`} type="number" min="1" max="365" disabled={!editable} value={draft.estimatedDurationDays} onChange={(e) => set('estimatedDurationDays', Number(e.target.value))} /></label>
    </div>
    {editable && <div className="work-actions"><button className="secondary" disabled={busy} onClick={() => onSave({ title: draft.title, description: draft.description, category: draft.category, priority: draft.priority, isMandatory: draft.isMandatory, executorType: draft.executorType, executorName: draft.executorName, estimatedLaborAmount: draft.estimatedLaborAmount, estimatedPartsAmount: draft.estimatedPartsAmount, currency: draft.currency, estimatedDurationDays: draft.estimatedDurationDays, comment: draft.comment })}>Сохранить работу</button><button className="link-button danger" onClick={() => setRemoving(true)}>Удалить</button></div>}
    {removing && <div className="remove-confirm">{work.isMandatory && <label>Причина исключения обязательного дефекта<textarea aria-label={`Причина исключения: ${work.sourceDefectTitle}`} value={reason} onChange={(e) => setReason(e.target.value)} /></label>}<button className="danger-button" disabled={work.isMandatory && !reason.trim()} onClick={() => onRemove(reason || undefined)}>Подтвердить удаление</button><button className="link-button dark" onClick={() => setRemoving(false)}>Отмена</button></div>}
  </article>
}

function AddWork({ plan, busy, onAdd }: { plan: ReconditioningPlan; busy: boolean; onAdd: (body: object) => void }) {
  const [open, setOpen] = useState(false)
  const sources = useMemo(() => Array.from(new Map(plan.works.map((x) => [x.sourceDefectId, x])).values()), [plan.works])
  const [sourceId, setSourceId] = useState(sources[0]?.sourceDefectId ?? '')
  const [title, setTitle] = useState('Дополнительная работа')
  if (!open) return <button className="secondary add-work" onClick={() => setOpen(true)}>+ Добавить работу</button>
  return <div className="add-work-form"><h3>Новая работа</h3><label>Исходный дефект<select aria-label="Исходный дефект новой работы" value={sourceId} onChange={(e) => setSourceId(e.target.value)}>{sources.map((x) => <option key={x.sourceDefectId} value={x.sourceDefectId}>{x.sourceDefectTitle}</option>)}</select></label><label>Название<input aria-label="Название новой работы" value={title} onChange={(e) => setTitle(e.target.value)} /></label><button className="primary" disabled={busy || !sourceId || !title.trim()} onClick={() => onAdd({ workId: crypto.randomUUID(), sourceDefectId: sourceId, title, description: title, category: 'Other', priority: 'Normal', isMandatory: false, executorType: 'Internal', executorName: 'Сервисный участок', estimatedLaborAmount: 0, estimatedPartsAmount: 0, currency: plan.budgets[0]?.currency ?? 'RUB', estimatedDurationDays: 1 })}>Добавить в план</button></div>
}

function BudgetCards({ budgets, approved }: { budgets: ReconditioningBudget[]; approved?: number }) {
  return <div className="budget-cards">{budgets.length ? budgets.map((budget) => <div className="metric budget" key={budget.currency}><span>{approved === undefined ? 'Плановый бюджет' : 'Одобренный лимит'}</span><strong>{money(approved ?? budget.totalAmount, budget.currency)}</strong><small>работы {money(budget.laborAmount, budget.currency)} · запчасти {money(budget.partsAmount, budget.currency)}</small></div>) : <div className="metric budget"><span>Плановый бюджет</span><strong>—</strong><small>Добавьте работы</small></div>}</div>
}

function History({ plan }: { plan: ReconditioningPlan }) {
  return <div className="approval-history"><h3>История согласования</h3>{plan.history.map((item) => <div key={item.id}><i /><p><strong>{statusLabel(item.toStatus as PlanStatus)}</strong><span>{item.actorName} · {new Date(item.occurredAt).toLocaleString('ru-RU')}</span>{item.reason && <small>{item.reason}</small>}</p></div>)}</div>
}

function RevisionComparison({ current, previous }: { current: ReconditioningPlan; previous: ReconditioningPlan }) {
  const currentTotal = current.budgets.reduce((sum, x) => sum + x.totalAmount, 0)
  const previousTotal = previous.approvedBudgetSnapshots.at(-1)?.approvedLimitAmount ?? previous.budgets.reduce((sum, x) => sum + x.totalAmount, 0)
  return <section className="revision-comparison"><div><p className="eyebrow">Сравнение ревизий</p><h2>Ревизия {previous.revision} → {current.revision}</h2></div><dl><div><dt>Работы</dt><dd>{previous.works.length} → {current.works.length}</dd></div><div><dt>Бюджет</dt><dd>{previousTotal.toLocaleString('ru-RU')} → {currentTotal.toLocaleString('ru-RU')}</dd></div><div><dt>Изменение</dt><dd className={currentTotal > previousTotal ? 'negative' : 'positive'}>{(currentTotal - previousTotal).toLocaleString('ru-RU')}</dd></div></dl></section>
}

function budgetTotal(budgets: ReconditioningBudget[]) { return budgets.map((x) => money(x.totalAmount, x.currency)).join(' + ') || 'Без бюджета' }
function statusLabel(status: PlanStatus) { return ({ Draft: 'Черновик', Submitted: 'На согласовании', ChangesRequested: 'На доработке', Approved: 'Утверждён', Rejected: 'Отклонён', Cancelled: 'Отменён' } as const)[status] }
function categoryLabel(category: string) { return ({ Body: 'Кузов', Mechanical: 'Механика', Interior: 'Салон', Electrical: 'Электрика', WheelsAndTires: 'Колёса и шины', Detailing: 'Детейлинг', Documents: 'Документы', Other: 'Другое' } as Record<string, string>)[category] ?? category }
function priorityLabel(priority: string) { return ({ Low: 'Низкий', Normal: 'Обычный', High: 'Высокий', Critical: 'Критический' } as Record<string, string>)[priority] ?? priority }
