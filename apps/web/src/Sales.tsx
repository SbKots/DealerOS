import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, ApiError, getSession } from './api'
import type { Lead } from './Crm'

export type Visit = {
  id: string; branchId: string; customerId: string; customerName: string; leadId: string; vehicleId: string
  vehicleName: string; responsibleUserId: string; responsibleName: string; status: string; startsAt: string
  endsAt: string; includesTestDrive: boolean; driverDocumentsChecked: boolean; issueChecklist?: string
  returnChecklist?: string; odometerOutKm?: number; odometerInKm?: number; checkedOutAt?: string; checkedInAt?: string
  incidentOccurred: boolean; result?: string; nextAction?: string; version: number
  history: { commandId: string; operation: string; occurredAt: string }[]
}
export type OfferPreview = {
  basePriceAmount: number; lineItemsAmount: number; discountAmount: number; finalPriceAmount: number
  costSnapshotAmount: number; expectedMarginAmount: number; minimumMarginAmount: number; currency: string
  belowMinimumMargin: boolean; requiresManagerApproval: boolean; autoApprovalDiscountLimit: number
}
export type SalesOffer = OfferPreview & {
  id: string; branchId: string; customerId: string; customerName: string; leadId: string; vehicleId: string
  vehicleName: string; revisesOfferId?: string; revision: number; status: string; validUntil: string; version: number
  lineItems: { id: string; category: string; name: string; amount: number; currency: string }[]
  decisions: { decisionId: string; decision: string; reason: string; occurredAt: string }[]
  history: { commandId: string; operation: string; occurredAt: string }[]
  approvedSnapshot?: { id: string; revision: number; finalPriceAmount: number; costSnapshotAmount: number; expectedMarginAmount: number; lineItemsJson: string; approvedAt: string }
}

type OfferLine = { id: string; category: string; name: string; amount: number; currency: string }
const newOfferLine = (): OfferLine => ({ id: crypto.randomUUID(), category: 'Equipment', name: '', amount: 0, currency: 'RUB' })

const tomorrow = new Date(Date.now() + 86_400_000)
const dateValue = (date: Date) => new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)

export function SalesWorkspace() {
  const permissions = getSession()?.permissions ?? []
  return <>
    <div className="page-heading"><div><p className="eyebrow">Итерация 0.7</p><h1>Визиты и согласованные предложения</h1><p className="muted">Календарь не является бронью: test-drive slot защищён отдельно, а Approved Offer хранится неизменяемым snapshot.</p></div></div>
    <div className="sales-grid">
      {permissions.includes('sales.visits.view') && <VisitsWorkspace />}
      {permissions.includes('sales.offers.view') && <OffersWorkspace />}
    </div>
  </>
}

function VisitsWorkspace() {
  const client = useQueryClient()
  const [selected, setSelected] = useState<Visit | null>(null)
  const [leadId, setLeadId] = useState('')
  const [startsAt, setStartsAt] = useState(dateValue(tomorrow))
  const [endsAt, setEndsAt] = useState(dateValue(new Date(tomorrow.getTime() + 3_600_000)))
  const [includesTestDrive, setIncludesTestDrive] = useState(true)
  const [odometer, setOdometer] = useState(0)
  const [result, setResult] = useState('Клиент заинтересован в предложении')
  const [nextAction, setNextAction] = useState('Подготовить предложение')
  const leads = useQuery({ queryKey: ['crm-leads'], queryFn: () => api<Lead[]>('/api/crm/leads') })
  const visits = useQuery({ queryKey: ['sales-visits'], queryFn: () => api<Visit[]>('/api/sales/visits') })
  const qualified = (leads.data ?? []).filter((x) => x.status === 'Qualified' && x.vehicleId)
  const refresh = (visit: Visit) => { setSelected(visit); client.invalidateQueries({ queryKey: ['sales-visits'] }) }
  const create = useMutation({ mutationFn: () => api<Visit>('/api/sales/visits', { method: 'POST', body: { visitId: crypto.randomUUID(), leadId, startsAt: new Date(startsAt).toISOString(), endsAt: new Date(endsAt).toISOString(), includesTestDrive } }), onSuccess: refresh })
  const command = useMutation({ mutationFn: ({ action, body }: { action: string; body: object }) => api<Visit>(`/api/sales/visits/${selected!.id}/${action}`, { method: 'POST', body }), onSuccess: refresh })
  const run = (action: string, body: object = {}) => selected && command.mutate({ action, body: { commandId: crypto.randomUUID(), expectedVersion: selected.version, ...body } })
  const error = leads.error ?? visits.error ?? create.error ?? command.error

  return <section className="panel sales-panel"><div className="panel-title"><span className="step">09</span><div><h2>Календарь визитов</h2><p>Время хранится timezone-safe; пересечения автомобиля и менеджера проверяет PostgreSQL.</p></div></div>
    {error && <div className="error-banner" role="alert">{errorText(error)}</div>}
    {!selected ? <><div className="work-form sales-form"><label className="wide">Qualified Lead<select aria-label="Lead для визита" value={leadId} onChange={(e) => setLeadId(e.target.value)}><option value="">Выберите</option>{qualified.map((x) => <option key={x.id} value={x.id}>{x.customerName} · {x.searchCriteria ?? 'автомобиль'}</option>)}</select></label><label>Начало<input aria-label="Начало визита" type="datetime-local" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} /></label><label>Окончание<input aria-label="Окончание визита" type="datetime-local" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} /></label><label className="check-row"><input type="checkbox" checked={includesTestDrive} onChange={(e) => setIncludesTestDrive(e.target.checked)} />Test Drive</label><button className="primary" disabled={!leadId || create.isPending} onClick={() => create.mutate()}>Назначить визит</button></div>
      <div className="calendar-list">{visits.isLoading ? <p>Загрузка…</p> : visits.data?.length ? visits.data.map((visit) => <button key={visit.id} onClick={() => setSelected(visit)}><time>{dateTime(visit.startsAt)}</time><span><strong>{visit.customerName}</strong><small>{visit.vehicleName} · {visit.responsibleName}</small></span><span className={`status ${visit.status === 'Completed' ? 'success' : 'draft'}`}><i />{visit.status}</span></button>) : <div className="empty-state compact"><strong>Визитов пока нет</strong></div>}</div></> : <div className="visit-card"><button className="link-button" onClick={() => setSelected(null)}>← К календарю</button><div className="work-card-head"><div><span className="status draft"><i />{selected.status}</span><h3>{selected.customerName}</h3><p>{selected.vehicleName} · {dateTime(selected.startsAt)}</p></div><strong>{selected.responsibleName}</strong></div>
      {selected.status === 'Scheduled' && <div className="work-actions"><button className="primary" onClick={() => run('arrive')}>Клиент прибыл</button><button className="secondary" onClick={() => run('reschedule', { startsAt: new Date(startsAt).toISOString(), endsAt: new Date(endsAt).toISOString() })}>Перенести</button><button className="link-button danger" onClick={() => run('no-show', { reason: 'Клиент не приехал' })}>No Show</button></div>}
      {selected.status === 'Arrived' && selected.includesTestDrive && !selected.checkedOutAt && <div className="work-form"><div className="critical-banner wide"><strong>Проверка без хранения номера документа</strong><span>Подтвердите только факт проверки водительских документов.</span></div><label>Пробег до<input aria-label="Пробег до" type="number" value={odometer} onChange={(e) => setOdometer(Number(e.target.value))} /></label><button className="primary" onClick={() => run('test-drive/check-out', { driverDocumentsChecked: true, issueChecklist: 'Ключ, документы и кузов проверены', odometerOutKm: odometer, conditionOut: 'Без новых повреждений' })}>Выдать на test drive</button></div>}
      {selected.status === 'Arrived' && selected.checkedOutAt && !selected.checkedInAt && <div className="work-form"><label>Пробег после<input aria-label="Пробег после" type="number" value={odometer} onChange={(e) => setOdometer(Number(e.target.value))} /></label><button className="primary" onClick={() => run('test-drive/check-in', { returnChecklist: 'Ключ, документы и кузов возвращены', odometerInKm: odometer, conditionIn: 'Без новых повреждений', incidentOccurred: false, incidentComment: null })}>Принять автомобиль</button></div>}
      {selected.status === 'Arrived' && (!selected.includesTestDrive || selected.checkedInAt) && <div className="work-form"><label className="wide">Результат<textarea aria-label="Результат визита" value={result} onChange={(e) => setResult(e.target.value)} /></label><label className="wide">Следующее действие<input aria-label="Следующее действие визита" value={nextAction} onChange={(e) => setNextAction(e.target.value)} /></label><button className="primary" onClick={() => run('complete', { result, nextAction, nextActionDueAt: new Date(Date.now() + 86_400_000).toISOString() })}>Завершить визит</button></div>}
      <div className="timeline"><h3>История визита</h3>{selected.history.map((x) => <article key={x.commandId}><span>{dateTime(x.occurredAt)}</span><strong>{x.operation}</strong></article>)}</div>
    </div>}
  </section>
}

function OffersWorkspace() {
  const client = useQueryClient()
  const permissions = getSession()?.permissions ?? []
  const [selected, setSelected] = useState<SalesOffer | null>(null)
  const [leadId, setLeadId] = useState('')
  const [lineItems, setLineItems] = useState<OfferLine[]>(() => [{ ...newOfferLine(), name: 'Комплект зимних шин', amount: 20_000 }])
  const [discount, setDiscount] = useState(0)
  const [preview, setPreview] = useState<OfferPreview | null>(null)
  const [decisionReason, setDecisionReason] = useState('Экономика и полномочия проверены')
  const leads = useQuery({ queryKey: ['crm-leads'], queryFn: () => api<Lead[]>('/api/crm/leads') })
  const offers = useQuery({ queryKey: ['sales-offers'], queryFn: () => api<SalesOffer[]>('/api/sales/offers') })
  const qualified = (leads.data ?? []).filter((x) => x.status === 'Qualified' && x.vehicleId)
  const selectOffer = (offer: SalesOffer) => {
    setSelected(offer)
    setLeadId(offer.leadId)
    setLineItems(offer.lineItems.length ? offer.lineItems.map((line) => ({ ...line })) : [newOfferLine()])
    setDiscount(offer.discountAmount)
    setPreview(null)
  }
  const refresh = (offer: SalesOffer) => { selectOffer(offer); client.invalidateQueries({ queryKey: ['sales-offers'] }) }
  const changeLine = (id: string, change: Partial<OfferLine>) => setLineItems((current) => current.map((line) => line.id === id ? { ...line, ...change } : line))
  const calculate = useMutation({ mutationFn: () => api<OfferPreview>('/api/sales/offers/preview', { method: 'POST', body: { leadId, lineItems, discountAmount: discount } }), onSuccess: setPreview })
  const create = useMutation({ mutationFn: () => api<SalesOffer>('/api/sales/offers', { method: 'POST', body: { offerId: crypto.randomUUID(), leadId, validUntil: new Date(Date.now() + 10 * 86_400_000).toISOString(), lineItems, discountAmount: discount } }), onSuccess: refresh })
  const update = useMutation({ mutationFn: () => api<SalesOffer>(`/api/sales/offers/${selected!.id}`, { method: 'PUT', body: { commandId: crypto.randomUUID(), validUntil: selected!.validUntil, lineItems, discountAmount: discount, expectedVersion: selected!.version } }), onSuccess: refresh })
  const command = useMutation({ mutationFn: ({ action, body }: { action: string; body: object }) => api<SalesOffer>(`/api/sales/offers/${selected!.id}/${action}`, { method: 'POST', body }), onSuccess: refresh })
  const run = (action: string, body: object = {}) => selected && command.mutate({ action, body: { commandId: crypto.randomUUID(), expectedVersion: selected.version, ...body } })
  const decide = (decision: string) => selected && command.mutate({ action: 'decision', body: { decisionId: crypto.randomUUID(), decision, reason: decisionReason, expectedVersion: selected.version } })
  const error = leads.error ?? offers.error ?? calculate.error ?? create.error ?? update.error ?? command.error

  return <section className="panel sales-panel"><div className="panel-title"><span className="step">10</span><div><h2>Offer Builder</h2><p>Итог и маржа приходят только с сервера; отрицательная услуга не заменяет скидку.</p></div></div>
    {error && <div className="error-banner" role="alert">{errorText(error)}</div>}
    {!selected ? <><div className="work-form sales-form"><label className="wide">Qualified Lead<select aria-label="Lead для предложения" value={leadId} onChange={(e) => { setLeadId(e.target.value); setPreview(null) }}><option value="">Выберите</option>{qualified.map((x) => <option key={x.id} value={x.id}>{x.customerName}</option>)}</select></label><OfferLinesEditor lines={lineItems} onChange={changeLine} onAdd={() => setLineItems((current) => [...current, newOfferLine()])} onRemove={(id) => setLineItems((current) => current.filter((line) => line.id !== id))} /><label>Скидка<input aria-label="Скидка" type="number" min="0" value={discount} onChange={(e) => { setDiscount(Number(e.target.value)); setPreview(null) }} /></label><div className="work-actions wide"><button className="secondary" disabled={!leadId || calculate.isPending} onClick={() => calculate.mutate()}>Рассчитать на сервере</button><button className="primary" disabled={!preview || create.isPending} onClick={() => create.mutate()}>Создать Offer</button></div></div>
      {preview && <OfferEconomics preview={preview} />}
      <div className="offer-list">{offers.data?.map((offer) => <button key={offer.id} onClick={() => selectOffer(offer)}><span><strong>{offer.customerName}</strong><small>{offer.vehicleName} · rev. {offer.revision}</small></span><strong>{money(offer.finalPriceAmount, offer.currency)}</strong><span className={`status ${offer.status === 'Approved' ? 'success' : 'draft'}`}><i />{offer.status}</span></button>)}</div></> : <div className="offer-card"><button className="link-button" onClick={() => setSelected(null)}>← К предложениям</button><div className="work-card-head"><div><span className={`status ${selected.status === 'Approved' ? 'success' : 'draft'}`}><i />{selected.status} · rev. {selected.revision}</span><h3>{selected.customerName}</h3><p>{selected.vehicleName}</p></div><strong>{money(selected.finalPriceAmount, selected.currency)}</strong></div><OfferEconomics preview={selected} />
      {['Draft', 'ChangesRequested'].includes(selected.status) && <div className="work-form sales-form"><OfferLinesEditor lines={lineItems} onChange={changeLine} onAdd={() => setLineItems((current) => [...current, newOfferLine()])} onRemove={(id) => setLineItems((current) => current.filter((line) => line.id !== id))} /><label>Скидка<input aria-label="Скидка Draft Offer" type="number" min="0" value={discount} onChange={(e) => setDiscount(Number(e.target.value))} /></label><div className="work-actions wide"><button className="secondary" disabled={update.isPending} onClick={() => update.mutate()}>Сохранить Draft</button><button className="primary" onClick={() => run('submit')}>Отправить / auto-approve</button></div></div>}
      {selected.status === 'Submitted' && permissions.includes('sales.offers.approve') && <div className="decision-form"><label>Обоснование<textarea aria-label="Причина решения по Offer" value={decisionReason} onChange={(e) => setDecisionReason(e.target.value)} /></label><div className="work-actions"><button className="primary" onClick={() => decide('Approved')}>Утвердить Offer</button><button className="secondary" onClick={() => decide('ChangesRequested')}>Вернуть на доработку</button><button className="danger-button" onClick={() => decide('Rejected')}>Отклонить</button></div></div>}
      {selected.approvedSnapshot && <div className="snapshot-card"><span>Immutable Approved Offer</span><strong>{money(selected.approvedSnapshot.finalPriceAmount, selected.currency)}</strong><small>Себестоимость {money(selected.approvedSnapshot.costSnapshotAmount, selected.currency)} · маржа {money(selected.approvedSnapshot.expectedMarginAmount, selected.currency)}</small><pre>{selected.approvedSnapshot.lineItemsJson}</pre>{permissions.includes('sales.offers.edit') && <button className="secondary" onClick={() => run('revisions', { revisionId: crypto.randomUUID(), validUntil: new Date(Date.now() + 14 * 86_400_000).toISOString() })}>Создать новую revision</button>}</div>}
      <div className="timeline"><h3>История Offer</h3>{selected.history.map((x) => <article key={x.commandId}><span>{dateTime(x.occurredAt)}</span><strong>{x.operation}</strong></article>)}</div>
    </div>}
  </section>
}

function OfferEconomics({ preview }: { preview: OfferPreview }) {
  return <div className={`economics ${preview.belowMinimumMargin ? 'danger' : ''}`}><div><span>Публичная цена</span><strong>{money(preview.basePriceAmount, preview.currency)}</strong></div><div><span>Line items</span><strong>{money(preview.lineItemsAmount, preview.currency)}</strong></div><div><span>Скидка</span><strong>− {money(preview.discountAmount, preview.currency)}</strong></div><div><span>Итог</span><strong>{money(preview.finalPriceAmount, preview.currency)}</strong></div><div><span>Себестоимость snapshot</span><strong>{money(preview.costSnapshotAmount, preview.currency)}</strong></div><div><span>Ожидаемая маржа</span><strong>{money(preview.expectedMarginAmount, preview.currency)}</strong></div>{preview.requiresManagerApproval && <p className="wide">Требуется manager approval: скидка выше {money(preview.autoApprovalDiscountLimit, preview.currency)} или маржа ниже границы.</p>}</div>
}

function OfferLinesEditor({ lines, onChange, onAdd, onRemove }: {
  lines: OfferLine[]
  onChange: (id: string, change: Partial<OfferLine>) => void
  onAdd: () => void
  onRemove: (id: string) => void
}) {
  return <div className="offer-lines-editor wide"><div className="offer-lines-heading"><strong>Прозрачные строки</strong><button type="button" className="secondary" onClick={onAdd}>Добавить строку</button></div>{lines.length ? lines.map((line, index) => <div className="offer-line-row" key={line.id}><label>Категория<input aria-label={`Категория строки ${index + 1}`} value={line.category} onChange={(event) => onChange(line.id, { category: event.target.value })} /></label><label>Название<input aria-label={`Название строки ${index + 1}`} value={line.name} onChange={(event) => onChange(line.id, { name: event.target.value })} /></label><label>Стоимость<input aria-label={`Стоимость строки ${index + 1}`} type="number" min="0.01" step="0.01" value={line.amount} onChange={(event) => onChange(line.id, { amount: Number(event.target.value) })} /></label><button type="button" className="link-button danger" aria-label={`Удалить строку ${index + 1}`} onClick={() => onRemove(line.id)}>Удалить</button></div>) : <p className="muted">Дополнительных услуг и оборудования нет.</p>}</div>
}

function dateTime(value: string) { return new Date(value).toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'short' }) }
function money(value: number, currency: string) { return new Intl.NumberFormat('ru-RU', { style: 'currency', currency }).format(value) }
function errorText(error: unknown) { return error instanceof ApiError ? error.message : 'Не удалось выполнить операцию продаж.' }
