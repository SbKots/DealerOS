import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, apiBlob, ApiError, getSession } from './api'
import type { Reservation } from './Reservations'

export type Deal = {
  id: string; branchId: string; reservationId: string; approvedOfferSnapshotId: string; customerId: string
  leadId: string; vehicleId: string; customerName: string; vehicleSnapshotJson: string; lineItemsJson: string
  status: string; basePriceAmount: number; lineItemsAmount: number; discountAmount: number; finalTotalAmount: number
  costSnapshotAmount: number; expectedMarginAmount: number; currency: string; receivedTotal: number
  refundedTotal: number; netPaid: number; balance: number; createdAt: string; completedAt?: string
  closedAt?: string; closureReason?: string; version: number
  payments: { id: string; commandId: string; kind: string; status: string; amount: number; currency: string; manualReference: string; reason: string; occurredAt: string; recordedAt: string }[]
  documents: { id: string; commandId: string; type: string; number: string; templateName: string; templateVersion: number; revision: number; sourceDocumentId?: string; sha256: string; sizeBytes: number; reason?: string; generatedAt: string }[]
  handover?: { actualMileageKm: number; keysTransferred: boolean; documentsTransferred: boolean; equipmentTransferred: boolean; conditionConfirmed: boolean; issuerConfirmed: boolean; responsibleConfirmed: boolean; conditionNotes: string; comments?: string; completedAt: string }
  history: { commandId: string; operation: string; occurredAt: string }[]
}

export function DealsWorkspace() {
  const queryClient = useQueryClient()
  const permissions = getSession()?.permissions ?? []
  const [selected, setSelected] = useState<Deal | null>(null)
  const [reservationId, setReservationId] = useState('')
  const [paymentAmount, setPaymentAmount] = useState(0)
  const [mileage, setMileage] = useState(42_100)
  const [reason, setReason] = useState('Решение подтверждено сотрудником в demo')
  const reservations = useQuery({ queryKey: ['reservations'], queryFn: () => api<Reservation[]>('/api/reservations') })
  const deals = useQuery({ queryKey: ['deals'], queryFn: () => api<Deal[]>('/api/deals') })
  const activeReservations = (reservations.data ?? []).filter((item) => item.status === 'Active')
  const refresh = (deal: Deal) => {
    setSelected(deal); setPaymentAmount(Math.max(0, deal.balance))
    queryClient.invalidateQueries({ queryKey: ['deals'] }); queryClient.invalidateQueries({ queryKey: ['reservations'] })
    queryClient.invalidateQueries({ queryKey: ['vehicles'] })
  }
  const create = useMutation({ mutationFn: () => api<Deal>('/api/deals', { method: 'POST', body: {
    dealId: crypto.randomUUID(), commandId: crypto.randomUUID(), reservationId,
  } }), onSuccess: refresh })
  const command = useMutation({ mutationFn: ({ action, body }: { action: string; body: object }) =>
    api<Deal>(`/api/deals/${selected!.id}/${action}`, { method: 'POST', body }), onSuccess: refresh })
  const run = (action: string, body: object = {}) => selected && command.mutate({ action, body: {
    commandId: crypto.randomUUID(), expectedVersion: selected.version, ...body,
  } })
  const registerPayment = (kind: 'Payment' | 'Refund', amount: number) => run('payments', {
    paymentId: crypto.randomUUID(), kind, status: kind === 'Refund' ? 'Refunded' : 'Received', amount,
    currency: selected!.currency, manualReference: `DEMO-${kind.toUpperCase()}-${Date.now()}`, reason,
    occurredAt: new Date().toISOString(),
  })
  const generate = (type: 'SaleContract' | 'HandoverAct') => run('documents', {
    documentId: crypto.randomUUID(), type, reason: selected!.documents.some((x) => x.type === type)
      ? 'Новая demo-ревизия документа' : null,
  })
  const error = reservations.error ?? deals.error ?? create.error ?? command.error
  const blockers = selected ? [
    ...(selected.balance !== 0 ? [`Остаток оплаты: ${money(selected.balance, selected.currency)}`] : []),
    ...(!selected.documents.some((x) => x.type === 'SaleContract') ? ['Нет договора купли-продажи'] : []),
    ...(!selected.documents.some((x) => x.type === 'HandoverAct') ? ['Нет акта выдачи'] : []),
    ...(!selected.handover && selected.status === 'ReadyForHandover' ? ['Не завершён checklist выдачи'] : []),
  ] : []

  const download = async (documentId: string, number: string) => {
    const blob = await apiBlob(`/api/deals/${selected!.id}/documents/${documentId}`)
    const url = URL.createObjectURL(blob); const anchor = window.document.createElement('a')
    anchor.href = url; anchor.download = `${number}.pdf`; anchor.click(); URL.revokeObjectURL(url)
  }

  return <>
    <div className="page-heading"><div><p className="eyebrow">Итерация 0.9</p><h1>Сделка, оплата и выдача</h1><p className="muted">Deal строится из точного Approved Offer snapshot. Платежи и возвраты append-only; документы — private immutable PDF.</p></div><div className="metric"><span>Сделки</span><strong>{deals.data?.length ?? 0}</strong><small>в доступных филиалах</small></div></div>
    {error && <div className="error-banner workspace-error" role="alert">{errorText(error)}</div>}
    <div className="deal-layout">
      <section className="panel"><div className="panel-title"><span className="step">13</span><div><h2>Reservation → Deal</h2><p>Преобразование брони и блокировка автомобиля выполняются атомарно.</p></div></div>
        {permissions.includes('deals.create') && <div className="work-form deal-create"><label className="wide">Active Reservation<select aria-label="Active Reservation для сделки" value={reservationId} onChange={(event) => setReservationId(event.target.value)}><option value="">Выберите бронь</option>{activeReservations.map((item) => <option key={item.id} value={item.id}>{item.customerName} · {item.vehicleName}</option>)}</select></label><button className="primary" disabled={!reservationId || create.isPending} onClick={() => create.mutate()}>Создать Deal snapshot</button></div>}
        {!selected ? <div className="deal-list">{deals.data?.map((deal) => <button key={deal.id} onClick={() => refresh(deal)}><span><strong>{deal.customerName}</strong><small>{vehicleName(deal.vehicleSnapshotJson)}</small></span><strong>{money(deal.finalTotalAmount, deal.currency)}</strong><span className={`status ${deal.status === 'Completed' ? 'success' : deal.status.includes('Refund') || deal.status === 'Cancelled' ? 'danger' : 'draft'}`}><i />{deal.status}</span></button>)}</div> : <button className="link-button" onClick={() => setSelected(null)}>← К списку сделок</button>}
      </section>

      {selected && <section className="panel deal-workspace"><div className="work-card-head"><div><span className={`status ${selected.status === 'Completed' ? 'success' : 'draft'}`}><i />{selected.status}</span><h2>{selected.customerName}</h2><p>{vehicleName(selected.vehicleSnapshotJson)}</p></div><strong>{money(selected.finalTotalAmount, selected.currency)}</strong></div>
        <div className="economics"><div><span>Получено</span><strong>{money(selected.receivedTotal, selected.currency)}</strong></div><div><span>Возвращено</span><strong>{money(selected.refundedTotal, selected.currency)}</strong></div><div><span>Остаток</span><strong>{money(selected.balance, selected.currency)}</strong></div></div>
        {blockers.length > 0 && !['Completed', 'Cancelled', 'Refunded'].includes(selected.status) && <div className="warning-banner"><strong>Блокеры следующего этапа</strong>{blockers.map((item) => <span key={item}>{item}</span>)}</div>}
        {selected.status === 'Draft' && <button className="primary" onClick={() => run('begin-payment')}>Перейти к оплате</button>}
        {selected.status === 'AwaitingPayment' && <div className="deal-sections"><div className="decision-form"><h3>Manual payment ledger</h3><label>Сумма<input aria-label="Сумма платежа" type="number" min="0.01" value={paymentAmount} onChange={(event) => setPaymentAmount(Number(event.target.value))} /></label><label>Причина<input aria-label="Причина платежа" value={reason} onChange={(event) => setReason(event.target.value)} /></label><button className="primary" disabled={paymentAmount <= 0} onClick={() => registerPayment('Payment', paymentAmount)}>Зарегистрировать оплату</button></div><DocumentActions deal={selected} generate={generate} download={download} /><button className="primary" onClick={() => run('ready-for-handover')}>Проверить готовность к выдаче</button></div>}
        {selected.status === 'ReadyForHandover' && <div className="decision-form"><h3>Checklist выдачи</h3><label>Фактический пробег<input aria-label="Фактический пробег при выдаче" type="number" value={mileage} onChange={(event) => setMileage(Number(event.target.value))} /></label><p className="info-banner">Ключи, документы, оборудование, состояние, выдающий и ответственный подтверждаются обязательным immutable snapshot.</p>{!selected.handover ? <button className="primary" onClick={() => run('handover', { actualMileageKm: mileage, keysTransferred: true, documentsTransferred: true, equipmentTransferred: true, conditionConfirmed: true, issuerConfirmed: true, responsibleConfirmed: true, conditionNotes: 'Состояние соответствует Deal snapshot', comments: 'Demo handover' })}>Завершить checklist выдачи</button> : <button className="primary" onClick={() => run('complete')}>Выдать автомобиль и завершить Deal</button>}</div>}
        {selected.status === 'RefundPending' && <div className="decision-form"><h3>Требуется возврат</h3><p>Автомобиль останется SaleInProgress до полного возврата {money(selected.netPaid, selected.currency)}.</p><button className="danger-button" onClick={() => registerPayment('Refund', selected.netPaid)}>Зафиксировать полный возврат</button></div>}
        {['Draft', 'AwaitingPayment', 'ReadyForHandover'].includes(selected.status) && <div className="decision-form"><label>Причина отмены<input aria-label="Причина отмены сделки" value={reason} onChange={(event) => setReason(event.target.value)} /></label><button className="link-button danger" onClick={() => run('cancel', { reason })}>Отменить сделку</button></div>}
        {selected.status === 'Completed' && <div className="success-banner"><span>✓</span><div><strong>Автомобиль продан</strong><small>Deal и документы доступны только для чтения; активная публикация снята.</small></div></div>}
        {selected.status !== 'AwaitingPayment' && <DocumentActions deal={selected} generate={generate} download={download} readOnly />}
        <div className="payment-timeline"><h3>Платежи и возвраты</h3>{selected.payments.map((item) => <article key={item.id}><span>{dateTime(item.occurredAt)}</span><strong>{item.kind} · {item.status}</strong><b>{money(item.amount, item.currency)}</b><small>{item.manualReference}</small></article>)}</div>
      </section>}
    </div>
  </>
}

function DocumentActions({ deal, generate, download, readOnly = false }: { deal: Deal; generate: (type: 'SaleContract' | 'HandoverAct') => void; download: (id: string, number: string) => void; readOnly?: boolean }) {
  return <div className="document-grid">{(['SaleContract', 'HandoverAct'] as const).map((type) => { const docs = deal.documents.filter((x) => x.type === type); const latest = docs.at(-1); return <article key={type}><strong>{type === 'SaleContract' ? 'Договор купли-продажи' : 'Акт выдачи'}</strong>{latest ? <><small>{latest.number} · rev. {latest.revision}</small><button className="secondary" onClick={() => download(latest.id, latest.number)}>Скачать PDF</button></> : <small>Не сформирован</small>}{!readOnly && <button className="secondary" onClick={() => generate(type)}>{latest ? 'Новая ревизия' : 'Сформировать PDF'}</button>}</article> })}</div>
}

function vehicleName(json: string) { try { const value = JSON.parse(json) as { make?: string; model?: string; vin?: string }; return `${value.make ?? ''} ${value.model ?? ''} · ${value.vin ?? ''}`.trim() } catch { return 'Vehicle snapshot' } }
function dateTime(value: string) { return new Date(value).toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'short' }) }
function money(value: number, currency: string) { return new Intl.NumberFormat('ru-RU', { style: 'currency', currency }).format(value) }
function errorText(error: unknown) { return error instanceof ApiError ? error.message : 'Не удалось выполнить операцию сделки.' }
