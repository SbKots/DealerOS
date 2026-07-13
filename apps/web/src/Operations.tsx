import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, ApiError, getSession } from './api'
import type { ReconditioningQueueVehicle } from './Reconditioning'

type Material = { id: string; type: string; name: string; quantity: number; unit: string; unitCost: number; currency: string; signedAmount: number }
export type ExecutionWorkOrder = {
  id: string; title: string; isMandatory: boolean; executorType: string; assigneeName: string; dueAt: string; status: string
  plannedLaborAmount: number; plannedPartsAmount: number; actualLaborHours: number; actualLaborAmount: number
  actualMaterialAmount: number; actualExternalAmount: number; currency: string; settlementStatus: string; materials: Material[]
}
export type ReconditioningExecution = {
  id: string; vehicleId: string; planId: string; status: string; plannedAmount: number; approvedLimitAmount: number
  actualLaborAmount: number; actualMaterialAmount: number; actualExternalAmount: number; actualTotalAmount: number
  varianceAmount: number; currency: string; version: number; workOrders: ExecutionWorkOrder[]
  notifications: { id: string; workOrderId: string; type: string; createdAt: string }[]
}

type Command = { path: string; body: object }

export function OperationsWorkspace() {
  const client = useQueryClient()
  const [active, setActive] = useState<ReconditioningExecution | null>(null)
  const queue = useQuery({ queryKey: ['operations-ready'], queryFn: () => api<ReconditioningQueueVehicle[]>('/api/reconditioning-plans/required-vehicles') })
  const approved = queue.data?.filter((x) => x.latestPlanStatus === 'Approved' && x.latestPlanId) ?? []
  const refresh = (execution: ReconditioningExecution) => {
    setActive(execution)
    client.setQueryData(['operations-execution', execution.id], execution)
  }
  const create = useMutation({
    mutationFn: (planId: string) => api<ReconditioningExecution>('/api/operations/executions', { method: 'POST', body: { planId } }),
    onSuccess: refresh,
  })
  const command = useMutation({
    mutationFn: ({ path, body }: Command) => api<ReconditioningExecution>(path, { method: 'POST', body }),
    onSuccess: refresh,
  })
  const canApprove = getSession()?.permissions?.includes('operations.approve_overrun') ?? false

  if (active) return <ExecutionEditor execution={active} busy={command.isPending} canApprove={canApprove}
    onBack={() => setActive(null)} onCommand={(path, body) => command.mutate({ path, body })} error={command.error} />

  return <>
    <div className="page-heading"><div><p className="eyebrow">Итерация 0.4</p><h1>Выполнение подготовки</h1><p className="muted">Работы, фактические расходы и отклонение от утверждённого лимита.</p></div><div className="metric"><span>Готово к запуску</span><strong>{approved.length}</strong><small>утверждённых планов</small></div></div>
    {create.error && <div className="error-banner" role="alert">{errorText(create.error)}</div>}
    <section className="panel"><div className="panel-title"><span className="step">04</span><div><h2>Очередь исполнения</h2><p>Execution создаётся только из неизменяемого budget snapshot.</p></div></div>
      {queue.isLoading ? <p>Загрузка…</p> : approved.length ? <div className="queue-grid">{approved.map((vehicle) => <article className="queue-card" key={vehicle.vehicleId}><span className="status success"><i />План утверждён</span><h3>{vehicle.make} {vehicle.model}</h3><p className="mono">{vehicle.vin}</p><dl><div><dt>Филиал</dt><dd>{vehicle.branchName}</dd></div><div><dt>Ревизия</dt><dd>{vehicle.latestPlanRevision}</dd></div></dl><button className="primary" disabled={create.isPending} onClick={() => create.mutate(vehicle.latestPlanId!)}>Открыть выполнение</button></article>)}</div> : <div className="empty-state"><strong>Очередь пуста</strong><p>Сначала утвердите план предпродажной подготовки.</p></div>}
    </section>
  </>
}

function ExecutionEditor({ execution, busy, canApprove, onBack, onCommand, error }: { execution: ReconditioningExecution; busy: boolean; canApprove: boolean; onBack: () => void; onCommand: (path: string, body: object) => void; error: unknown }) {
  const base = `/api/operations/executions/${execution.id}`
  const overrun = execution.actualTotalAmount > execution.approvedLimitAmount
  return <>
    <div className="page-heading"><div><button className="link-button" onClick={onBack}>← К очереди</button><p className="eyebrow">Execution · {execution.status}</p><h1>Фактическое выполнение работ</h1></div><div className="metric"><span>Отклонение</span><strong>{money(execution.varianceAmount, execution.currency)}</strong><small>от утверждённого лимита</small></div></div>
    {error && <div className="error-banner" role="alert">{errorText(error)}</div>}
    <section className="panel"><div className="budget-grid"><Budget label="План" value={execution.plannedAmount} currency={execution.currency} /><Budget label="Лимит" value={execution.approvedLimitAmount} currency={execution.currency} /><Budget label="Факт" value={execution.actualTotalAmount} currency={execution.currency} /><Budget label="Материалы" value={execution.actualMaterialAmount} currency={execution.currency} /></div>
      {execution.status === 'Draft' && <button className="primary" disabled={busy} onClick={() => onCommand(`${base}/start`, { expectedVersion: execution.version })}>Начать выполнение</button>}
      {overrun && execution.status !== 'Completed' && <div className="critical-banner" role="status"><strong>Факт превышает утверждённый лимит.</strong><span>Завершение заблокировано до отдельного решения руководителя.</span>{canApprove && <button className="secondary" disabled={busy} onClick={() => onCommand(`${base}/approve-overrun`, { decisionId: crypto.randomUUID(), approvedLimitAmount: execution.actualTotalAmount, currency: execution.currency, reason: 'Согласовано в рабочем пространстве', expectedVersion: execution.version })}>Согласовать новый лимит</button>}</div>}
    </section>
    <section className="registry"><div><p className="eyebrow">Заказ-работы</p><h2>Состав утверждённого плана</h2></div>{execution.workOrders.map((work) => <WorkOrderCard key={`${work.id}-${execution.version}`} execution={execution} work={work} busy={busy} onCommand={onCommand} />)}</section>
    {execution.notifications.length > 0 && <section className="panel"><h2>Контроль сроков</h2>{execution.notifications.map((x) => <p key={x.id} className="critical-banner"><strong>{x.type === 'Overdue' ? 'Просрочено' : 'Срок близко'}</strong><span>{new Date(x.createdAt).toLocaleString('ru-RU')}</span></p>)}</section>}
    {execution.status !== 'Completed' && <button className="primary" disabled={busy || execution.workOrders.some((x) => x.isMandatory && x.status !== 'Completed')} onClick={() => onCommand(`${base}/complete`, { expectedVersion: execution.version })}>Завершить подготовку</button>}
  </>
}

function WorkOrderCard({ execution, work, busy, onCommand }: { execution: ReconditioningExecution; work: ExecutionWorkOrder; busy: boolean; onCommand: (path: string, body: object) => void }) {
  const [laborHours, setLaborHours] = useState(work.actualLaborHours)
  const [labor, setLabor] = useState(work.actualLaborAmount)
  const [external, setExternal] = useState(work.actualExternalAmount)
  const [material, setMaterial] = useState(0)
  const base = `/api/operations/executions/${execution.id}/work-orders/${work.id}`
  const mutable = execution.status !== 'Completed' && work.status !== 'Completed'
  return <article className="work-card"><div className="work-card-head"><div><span className="status draft"><i />{work.status}</span><h3>{work.title}</h3><p>{work.executorType} · срок {new Date(work.dueAt).toLocaleDateString('ru-RU')}</p></div><strong>{money(work.actualLaborAmount + work.actualMaterialAmount + work.actualExternalAmount, work.currency)}</strong></div>
    <div className="work-form"><label>Часы<input aria-label={`Часы: ${work.title}`} type="number" min="0" step="0.25" disabled={!mutable || busy} value={laborHours} onChange={(e) => setLaborHours(Number(e.target.value))} /></label><label>Работа<input aria-label={`Факт работы: ${work.title}`} type="number" min="0" disabled={!mutable || busy} value={labor} onChange={(e) => setLabor(Number(e.target.value))} /></label><label>Подрядчик<input aria-label={`Факт подрядчика: ${work.title}`} type="number" min="0" disabled={!mutable || busy} value={external} onChange={(e) => setExternal(Number(e.target.value))} /></label><label>Материал<input aria-label={`Материал: ${work.title}`} type="number" min="0" disabled={!mutable || busy} value={material} onChange={(e) => setMaterial(Number(e.target.value))} /></label></div>
    {mutable && <div className="work-actions">{work.status === 'Scheduled' && <button className="secondary" disabled={busy} onClick={() => onCommand(`${base}/start`, { expectedVersion: execution.version })}>Начать работу</button>}<button className="secondary" disabled={busy} onClick={() => onCommand(`${base}/actuals`, { laborHours, laborAmount: labor, externalAmount: external, contractorName: external > 0 ? 'Внешний подрядчик' : null, invoiceReference: null, contractorDueAt: null, expectedVersion: execution.version })}>Сохранить факт</button>{material > 0 && <button className="secondary" disabled={busy} onClick={() => onCommand(`${base}/materials`, { movementId: crypto.randomUUID(), type: 'Consumed', name: 'Материал', quantity: 1, unit: 'шт.', unitCost: material, currency: work.currency, supplierName: null, expectedVersion: execution.version })}>Списать материал</button>}<button className="primary" disabled={busy} onClick={() => onCommand(`${base}/complete`, { comment: 'Работа выполнена', expectedVersion: execution.version })}>Завершить работу</button></div>}
  </article>
}

function Budget({ label, value, currency }: { label: string; value: number; currency: string }) { return <div className="budget-card"><span>{label}</span><strong>{money(value, currency)}</strong></div> }
function money(value: number, currency: string) { return new Intl.NumberFormat('ru-RU', { style: 'currency', currency }).format(value) }
function errorText(error: unknown) { return error instanceof ApiError ? error.message : 'Не удалось выполнить операцию.' }
