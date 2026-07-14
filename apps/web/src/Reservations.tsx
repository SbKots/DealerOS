import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { api, ApiError, getSession } from './api'
import type { SalesOffer } from './Sales'

export type Reservation = {
  id: string; branchId: string; vehicleId: string; vehicleName: string; customerId: string; customerName: string
  leadId: string; approvedOfferSnapshotId: string; status: string; depositStatus: string; depositAmount: number
  currency: string; depositReference?: string; createdAt: string; startsAt: string; expiresAt: string
  closedAt?: string; closureReason?: string; version: number
  history: { commandId: string; operation: string; occurredAt: string }[]
}

const localDateTime = (date: Date) => new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  .toISOString().slice(0, 16)

export function ReservationsWorkspace() {
  const queryClient = useQueryClient()
  const permissions = getSession()?.permissions ?? []
  const [selected, setSelected] = useState<Reservation | null>(null)
  const [snapshotId, setSnapshotId] = useState('')
  const [expiresAt, setExpiresAt] = useState(localDateTime(new Date(Date.now() + 24 * 60 * 60_000)))
  const [depositRequired, setDepositRequired] = useState(false)
  const [depositAmount, setDepositAmount] = useState(100_000)
  const [reason, setReason] = useState('Согласовано с клиентом')
  const [now, setNow] = useState(Date.now())

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1_000)
    return () => window.clearInterval(timer)
  }, [])

  const offers = useQuery({
    queryKey: ['sales-offers'],
    queryFn: () => api<SalesOffer[]>('/api/sales/offers'),
    enabled: permissions.includes('reservations.create'),
  })
  const reservations = useQuery({ queryKey: ['reservations'], queryFn: () => api<Reservation[]>('/api/reservations') })
  const approvedOffers = useMemo(() => (offers.data ?? []).filter((offer) =>
    offer.status === 'Approved' && offer.approvedSnapshot && new Date(offer.validUntil).getTime() > now),
  [offers.data, now])

  const refresh = (reservation: Reservation) => {
    setSelected(reservation)
    queryClient.invalidateQueries({ queryKey: ['reservations'] })
    queryClient.invalidateQueries({ queryKey: ['vehicles'] })
  }
  const create = useMutation({
    mutationFn: () => api<Reservation>('/api/reservations', {
      method: 'POST',
      body: {
        reservationId: crypto.randomUUID(), commandId: crypto.randomUUID(), approvedOfferSnapshotId: snapshotId,
        expiresAt: new Date(expiresAt).toISOString(), depositRequired,
        depositAmount: depositRequired ? depositAmount : 0,
        currency: approvedOffers.find((offer) => offer.approvedSnapshot?.id === snapshotId)?.currency ?? 'RUB',
      },
    }),
    onSuccess: refresh,
  })
  const command = useMutation({
    mutationFn: ({ action, body }: { action: string; body: object }) => api<Reservation>(
      `/api/reservations/${selected!.id}/${action}`, { method: 'POST', body }),
    onSuccess: refresh,
  })
  const run = (action: string, body: object = {}) => selected && command.mutate({
    action, body: { commandId: crypto.randomUUID(), expectedVersion: selected.version, ...body },
  })
  const error = offers.error ?? reservations.error ?? create.error ?? command.error

  return <>
    <div className="page-heading"><div><p className="eyebrow">Продажи</p><h1>Бронирования автомобилей</h1><p className="muted">Бронирование создаётся из действующего утверждённого предложения. Автомобиль защищён от одновременной продажи другому клиенту.</p></div><div className="metric"><span>Активные</span><strong>{reservations.data?.filter((x) => ['PendingDeposit', 'Active'].includes(x.status)).length ?? 0}</strong><small>временных блокировок автомобиля</small></div></div>
    {error && <div className="error-banner workspace-error" role="alert">{errorText(error)}</div>}
    <div className="reservation-layout">
      <section className="panel"><div className="panel-title"><span className="step">11</span><div><h2>Новая бронь</h2><p>Клиент и автомобиль берутся из неизменяемого предложения.</p></div></div>
        {permissions.includes('reservations.create') ? <div className="work-form reservation-form">
          <label className="wide">Утверждённое предложение<select aria-label="Утверждённое предложение для брони" value={snapshotId} onChange={(event) => setSnapshotId(event.target.value)}><option value="">Выберите предложение</option>{approvedOffers.map((offer) => <option key={offer.approvedSnapshot!.id} value={offer.approvedSnapshot!.id}>{offer.customerName} · {offer.vehicleName} · {money(offer.finalPriceAmount, offer.currency)}</option>)}</select></label>
          <label>Срок брони<input aria-label="Срок брони" type="datetime-local" value={expiresAt} onChange={(event) => setExpiresAt(event.target.value)} /></label>
          <label className="check-row"><input type="checkbox" checked={depositRequired} onChange={(event) => setDepositRequired(event.target.checked)} />Требуется предоплата</label>
          {depositRequired && <label>Предоплата<input aria-label="Сумма предоплаты" type="number" min="0.01" step="0.01" value={depositAmount} onChange={(event) => setDepositAmount(Number(event.target.value))} /></label>}
          <button className="primary" disabled={!snapshotId || create.isPending} onClick={() => create.mutate()}>{create.isPending ? 'Бронируем…' : 'Создать бронь'}</button>
        </div> : <div className="info-banner">Нет права создавать бронь. Доступен только просмотр очереди.</div>}
        {offers.isLoading || reservations.isLoading ? <p>Загрузка…</p> : null}
        {!approvedOffers.length && !offers.isLoading && permissions.includes('reservations.create') && <div className="empty-state compact"><strong>Нет доступных утверждённых предложений</strong><p>Сначала завершите визит и утвердите предложение.</p></div>}
      </section>

      <section className="panel"><div className="panel-title"><span className="step">12</span><div><h2>Активные и истекающие</h2><p>Таймер информационный; окончательное решение принимает сервер.</p></div></div>
        {!selected ? <div className="reservation-list">{reservations.data?.length ? reservations.data.map((reservation) => <button key={reservation.id} onClick={() => setSelected(reservation)}><span><strong>{reservation.customerName}</strong><small>{reservation.vehicleName}</small></span><Countdown expiresAt={reservation.expiresAt} closed={Boolean(reservation.closedAt)} now={now} /><span className={`status ${['Active', 'ConvertedToDeal'].includes(reservation.status) ? 'success' : reservation.status === 'PendingDeposit' ? 'draft' : 'danger'}`}><i />{reservationStatusLabel(reservation.status)}</span></button>) : !reservations.isLoading && <div className="empty-state compact"><strong>Броней пока нет</strong></div>}</div> : <div className="reservation-card">
          <button className="link-button" onClick={() => setSelected(null)}>← К очереди</button>
          <div className="work-card-head"><div><span className={`status ${selected.status === 'Active' ? 'success' : 'draft'}`}><i />{reservationStatusLabel(selected.status)}</span><h3>{selected.customerName}</h3><p>{selected.vehicleName}</p></div><Countdown expiresAt={selected.expiresAt} closed={Boolean(selected.closedAt)} now={now} /></div>
          <div className="economics"><div><span>Предоплата</span><strong>{money(selected.depositAmount, selected.currency)}</strong></div><div><span>Статус предоплаты</span><strong>{depositStatusLabel(selected.depositStatus)}</strong></div><div><span>Основание</span><strong>Утверждённое предложение</strong></div></div>
          {selected.status === 'PendingDeposit' && permissions.includes('reservations.deposit') && <div className="work-actions"><button className="primary" onClick={() => run('deposit', { status: 'Received', manualReference: `DEMO-${Date.now()}` })}>Предоплата получена</button><button className="danger-button" onClick={() => run('deposit', { status: 'Failed', reason: 'Предоплата не получена' })}>Предоплата не прошла</button></div>}
          {['PendingDeposit', 'Active'].includes(selected.status) && <div className="decision-form"><label>Причина<input aria-label="Причина изменения брони" value={reason} onChange={(event) => setReason(event.target.value)} /></label><div className="work-actions"><button className="secondary" disabled={!permissions.includes('reservations.extend')} onClick={() => run('extend', { expiresAt: new Date(Date.now() + 48 * 60 * 60_000).toISOString(), reason })}>Продлить на 48 часов</button><button className="danger-button" disabled={!permissions.includes('reservations.cancel')} onClick={() => run('cancel', { reason })}>Отменить бронь</button></div></div>}
          <div className="timeline"><h3>История брони</h3>{selected.history.map((item) => <article key={item.commandId}><span>{dateTime(item.occurredAt)}</span><strong>{item.operation}</strong></article>)}</div>
        </div>}
      </section>
    </div>
  </>
}

function Countdown({ expiresAt, closed, now }: { expiresAt: string; closed: boolean; now: number }) {
  if (closed) return <strong className="countdown closed">Закрыта</strong>
  const seconds = Math.max(0, Math.floor((new Date(expiresAt).getTime() - now) / 1_000))
  const hours = Math.floor(seconds / 3_600)
  const minutes = Math.floor((seconds % 3_600) / 60)
  return <strong className={`countdown ${seconds < 3_600 ? 'urgent' : ''}`}>{seconds ? `${hours} ч ${minutes} мин` : 'Срок истёк'}</strong>
}

function dateTime(value: string) { return new Date(value).toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'short' }) }
function reservationStatusLabel(status: string) { return ({ PendingDeposit: 'Ожидает предоплаты', Active: 'Активна', ConvertedToDeal: 'Переведена в сделку', Expired: 'Истекла', Cancelled: 'Отменена' } as Record<string, string>)[status] ?? status }
function depositStatusLabel(status: string) { return ({ NotRequired: 'Не требуется', Pending: 'Ожидается', Received: 'Получена', Failed: 'Не получена', Refunded: 'Возвращена' } as Record<string, string>)[status] ?? status }
function money(value: number, currency: string) { return new Intl.NumberFormat('ru-RU', { style: 'currency', currency }).format(value) }
function errorText(error: unknown) {
  if (error instanceof ApiError && error.status === 409) return `Конфликт бронирования: ${error.message}`
  return error instanceof ApiError ? error.message : 'Не удалось выполнить операцию с бронью.'
}
