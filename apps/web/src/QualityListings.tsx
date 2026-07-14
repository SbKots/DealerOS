import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { api, apiBlob, apiForm, ApiError, getSession } from './api'
import type { Vehicle } from './App'

type QueueWork = { id: string; sourceDefectId: string; title: string }
export type QualityQueue = { executionId: string; vehicleId: string; vin: string; make: string; model: string; completedAt: string; actualTotalAmount: number; currency: string; latestQualityCheckId?: string; latestQualityStatus?: string; latestQualityRevision?: number; workOrders: QueueWork[] }
export type QualityCheck = { id: string; vehicleId: string; executionId: string; revision: number; status: string; checklistSnapshotJson: string; decisionComment?: string; version: number; observations: { id: string; severity: string; workOrderId?: string; defectId?: string; comment: string; requiresRework: boolean }[] }
type Media = { id: string; vehicleId: string; category: string; originalFileName: string; sortOrder: number; isCover: boolean; version: number; downloadUrl: string }
export type Listing = { id: string; vehicleId: string; revision: number; status: string; vehicleMake: string; vehicleModel: string; vehicleYear: number; mileageKm: number; equipment: string; advantages: string; conditionDescription: string; publicPriceAmount: number; currency: string; templateName: string; templateVersion: number; version: number; publications: { id: string; channel: string; status: string; externalId?: string; error?: string }[] }

export function QualityListingsWorkspace() {
  const permissions = getSession()?.permissions ?? []
  return <>
    <div className="page-heading"><div><p className="eyebrow">Автомобили</p><h1>Качество и контент</h1><p className="muted">Готовность к продаже подтверждается независимым контролем качества; подготовка выгрузки не считается публикацией.</p></div></div>
    {permissions.includes('quality.view') && <QualityWorkspace />}
    {permissions.includes('listings.view') && <ListingWorkspace />}
  </>
}

function QualityWorkspace() {
  const client = useQueryClient()
  const [selected, setSelected] = useState<QualityQueue | null>(null)
  const [quality, setQuality] = useState<QualityCheck | null>(null)
  const [comment, setComment] = useState('')
  const [workOrderId, setWorkOrderId] = useState('')
  const queue = useQuery({ queryKey: ['quality-queue'], queryFn: () => api<QualityQueue[]>('/api/quality-checks/queue') })
  const refresh = (next: QualityCheck) => { setQuality(next); client.invalidateQueries({ queryKey: ['quality-queue'] }) }
  const create = useMutation({ mutationFn: (item: QualityQueue) => api<QualityCheck>(`/api/quality-checks/executions/${item.executionId}`, { method: 'POST' }), onSuccess: refresh })
  const command = useMutation({ mutationFn: ({ path, body }: { path: string; body: object }) => api<QualityCheck>(path, { method: 'POST', body }), onSuccess: refresh })
  const open = (item: QualityQueue) => { setSelected(item); setWorkOrderId(item.workOrders[0]?.id ?? ''); if (item.latestQualityCheckId) api<QualityCheck>(`/api/quality-checks/${item.latestQualityCheckId}`).then(setQuality); else create.mutate(item) }
  const addRework = () => selected && quality && command.mutate({ path: `/api/quality-checks/${quality.id}/observations`, body: { observationId: crypto.randomUUID(), severity: 'Major', workOrderId, defectId: selected.workOrders.find((x) => x.id === workOrderId)?.sourceDefectId, comment, requiresRework: true, expectedVersion: quality.version } })

  return <section className="panel"><div className="panel-title"><span className="step">05</span><div><h2>Независимый контроль качества</h2><p>Каждая повторная проверка сохраняется отдельной ревизией.</p></div></div>
    {(create.error || command.error) && <div className="error-banner" role="alert">{errorText(create.error ?? command.error)}</div>}
    {!quality ? queue.isLoading ? <p>Загрузка…</p> : queue.data?.length ? <div className="queue-grid">{queue.data.map((item) => <article className="queue-card" key={item.executionId}><span className="status draft"><i />{qualityStatusLabel(item.latestQualityStatus ?? 'Draft')}</span><h3>{item.make} {item.model}</h3><p className="mono">{item.vin}</p><dl><div><dt>Факт подготовки</dt><dd>{money(item.actualTotalAmount, item.currency)}</dd></div><div><dt>Работ</dt><dd>{item.workOrders.length}</dd></div></dl><button className="primary" onClick={() => open(item)}>Открыть контроль качества</button></article>)}</div> : <div className="empty-state"><strong>Очередь контроля качества пуста</strong><p>Нет завершённых подготовок, ожидающих решения.</p></div> : <div>
      <button className="link-button" onClick={() => { setQuality(null); setSelected(null) }}>← К очереди</button><div className="work-card-head"><div><span className="status draft"><i />Контроль, рев. {quality.revision} · {qualityStatusLabel(quality.status)}</span><h3>Контрольный снимок и замечания</h3></div></div>
      <details><summary>Технические данные контрольного снимка</summary><pre className="snapshot-preview">{JSON.stringify(JSON.parse(quality.checklistSnapshotJson), null, 2)}</pre></details>
      {quality.observations.map((x) => <div className="critical-banner" key={x.id}><strong>{x.severity}</strong><span>{x.comment}</span></div>)}
      {quality.status === 'Draft' && selected && <><div className="work-form"><label>Работа<select aria-label="Работа для возврата" value={workOrderId} onChange={(e) => setWorkOrderId(e.target.value)}>{selected.workOrders.map((x) => <option value={x.id} key={x.id}>{x.title}</option>)}</select></label><label className="wide">Замечание<textarea aria-label="Замечание контроля качества" value={comment} onChange={(e) => setComment(e.target.value)} /></label></div><div className="work-actions"><button className="secondary" disabled={!comment || command.isPending} onClick={addRework}>Добавить замечание</button><button className="secondary" disabled={command.isPending || !quality.observations.some((x) => x.requiresRework)} onClick={() => command.mutate({ path: `/api/quality-checks/${quality.id}/rework`, body: { comment: 'Вернуть выбранные работы', expectedVersion: quality.version } })}>Вернуть на исправление</button><button className="primary" disabled={command.isPending || quality.observations.some((x) => x.requiresRework)} onClick={() => command.mutate({ path: `/api/quality-checks/${quality.id}/pass`, body: { comment: 'Контроль качества пройден', expectedVersion: quality.version } })}>Подтвердить готовность к продаже</button></div></>}
    </div>}
  </section>
}

function ListingWorkspace() {
  const client = useQueryClient()
  const [vehicle, setVehicle] = useState<Vehicle | null>(null)
  const [listing, setListing] = useState<Listing | null>(null)
  const [draft, setDraft] = useState({ equipment: '', advantages: '', conditionDescription: '', publicPriceAmount: 0 })
  const [channel, setChannel] = useState('demo-channel')
  const [exportPayload, setExportPayload] = useState('')
  const vehicles = useQuery({ queryKey: ['vehicles'], queryFn: () => api<Vehicle[]>('/api/vehicles') })
  const ready = vehicles.data?.filter((x) => x.status === 'ReadyForSale') ?? []
  const media = useQuery({ queryKey: ['vehicle-media', vehicle?.id], queryFn: () => api<Media[]>(`/api/vehicles/${vehicle!.id}/media`), enabled: Boolean(vehicle) })
  useEffect(() => { if (!vehicle) return; api<Listing | null>(`/api/vehicles/${vehicle.id}/listing`).then((next) => { setListing(next); if (next) setDraft({ equipment: next.equipment, advantages: next.advantages, conditionDescription: next.conditionDescription, publicPriceAmount: next.publicPriceAmount }) }) }, [vehicle])
  const refresh = (next: Listing) => { setListing(next); client.invalidateQueries({ queryKey: ['vehicles'] }) }
  const create = useMutation({ mutationFn: () => api<Listing>(`/api/vehicles/${vehicle!.id}/listing`, { method: 'POST' }), onSuccess: refresh })
  const update = useMutation({ mutationFn: () => api<Listing>(`/api/listings/${listing!.id}`, { method: 'PUT', body: { commandId: crypto.randomUUID(), ...draft, currency: 'RUB', templateName: 'DealerOS Default', templateVersion: 1, expectedVersion: listing!.version } }), onSuccess: refresh })
  const command = useMutation({ mutationFn: ({ action, body }: { action: string; body: object }) => api<Listing>(`/api/listings/${listing!.id}/${action}`, { method: 'POST', body }), onSuccess: refresh })
  const upload = async (category: string, file: File) => { const body = new FormData(); body.append('mediaId', crypto.randomUUID()); body.append('category', category); body.append('file', file); await apiForm(`/api/vehicles/${vehicle!.id}/media`, body); await client.invalidateQueries({ queryKey: ['vehicle-media', vehicle!.id] }) }
  const doExport = async () => { const result = await api<{ payload: string; listing: Listing }>(`/api/listings/${listing!.id}/export`, { method: 'POST', body: { commandId: crypto.randomUUID(), channel } }); setExportPayload(result.payload); refresh(result.listing) }

  return <section className="panel"><div className="panel-title"><span className="step">06</span><div><h2>Медиа и объявление</h2><p>Оригиналы фотографий приватны; внутренние документы не попадают в публичную версию.</p></div></div>
    {(create.error || update.error || command.error) && <div className="error-banner" role="alert">{errorText(create.error ?? update.error ?? command.error)}</div>}
    {!vehicle ? ready.length ? <div className="queue-grid">{ready.map((item) => <article className="queue-card" key={item.id}><span className="status success"><i />Готов к продаже</span><h3>{item.make} {item.model}</h3><p className="mono">{item.vin}</p><button className="primary" onClick={() => setVehicle(item)}>Подготовить объявление</button></article>)}</div> : <div className="empty-state"><strong>Нет готовых к продаже автомобилей</strong><p>Автомобиль появится после успешного контроля качества.</p></div> : <div><button className="link-button" onClick={() => { setVehicle(null); setListing(null) }}>← К автомобилям</button><h3>{vehicle.make} {vehicle.model}</h3>
      <div className="media-grid">{media.data?.map((item) => <article className="media-card" key={item.id}><PrivateImage media={item} /><strong>{mediaCategoryLabel(item.category)}{item.isCover ? ' · Обложка' : ''}</strong>{!item.isCover && !['Documents', 'DocumentsInternal'].includes(item.category) && <button className="secondary" onClick={() => api<Media>(`/api/vehicles/${vehicle.id}/media/${item.id}/cover`, { method: 'POST', body: { expectedVersion: item.version } }).then(() => client.invalidateQueries({ queryKey: ['vehicle-media', vehicle.id] }))}>Сделать обложкой</button>}</article>)}</div>
      <div className="work-form">{['Exterior', 'Interior', 'DamageHistory', 'DocumentsInternal'].map((category) => <label key={category}>{mediaCategoryLabel(category)}<input aria-label={`Фото: ${mediaCategoryLabel(category)}`} type="file" accept="image/jpeg,image/png,image/webp" onChange={(e) => e.target.files?.[0] && upload(category, e.target.files[0])} /></label>)}</div>
      {!listing ? <button className="primary" disabled={create.isPending} onClick={() => create.mutate()}>Создать карточку объявления</button> : <><span className="status draft"><i />Объявление · {listingStatusLabel(listing.status)}</span><div className="work-form"><label>Комплектация<textarea aria-label="Комплектация" disabled={listing.status === 'Ready'} value={draft.equipment} onChange={(e) => setDraft({ ...draft, equipment: e.target.value })} /></label><label>Преимущества<textarea aria-label="Преимущества" disabled={listing.status === 'Ready'} value={draft.advantages} onChange={(e) => setDraft({ ...draft, advantages: e.target.value })} /></label><label className="wide">Состояние<textarea aria-label="Описание состояния" disabled={listing.status === 'Ready'} value={draft.conditionDescription} onChange={(e) => setDraft({ ...draft, conditionDescription: e.target.value })} /></label><label>Публичная цена<input aria-label="Публичная цена" type="number" disabled={listing.status === 'Ready'} value={draft.publicPriceAmount} onChange={(e) => setDraft({ ...draft, publicPriceAmount: Number(e.target.value) })} /></label></div>
        {listing.status === 'Draft' ? <div className="work-actions"><button className="secondary" onClick={() => update.mutate()}>Сохранить карточку</button><button className="primary" onClick={() => command.mutate({ action: 'ready', body: { commandId: crypto.randomUUID(), expectedVersion: listing.version } })}>Проверить и подготовить к публикации</button></div> : <><div className="work-actions"><label>Канал<input aria-label="Канал публикации" value={channel} onChange={(e) => setChannel(e.target.value)} /></label><button className="secondary" onClick={doExport}>Экспортировать JSON</button><button className="primary" onClick={() => command.mutate({ action: 'publish', body: { commandId: crypto.randomUUID(), channel, externalId: 'MANUAL', externalUrl: null } })}>Подтвердить публикацию вручную</button></div>{exportPayload && <details><summary>Технические данные экспорта</summary><pre className="snapshot-preview">{JSON.stringify(JSON.parse(exportPayload), null, 2)}</pre></details>}{listing.publications.map((x) => <p className="history-item" key={x.id}><strong>{x.channel}</strong> · {publicationStatusLabel(x.status)}</p>)}</>}
      </>}</div>}
  </section>
}

function PrivateImage({ media }: { media: Media }) {
  const [objectUrl, setObjectUrl] = useState('')
  const [failed, setFailed] = useState(false)
  useEffect(() => {
    let active = true
    let createdUrl = ''
    setObjectUrl('')
    setFailed(false)
    apiBlob(media.downloadUrl).then((blob) => {
      if (!active) return
      createdUrl = URL.createObjectURL(blob)
      setObjectUrl(createdUrl)
    }).catch(() => { if (active) setFailed(true) })
    return () => {
      active = false
      if (createdUrl) URL.revokeObjectURL(createdUrl)
    }
  }, [media.downloadUrl])
  if (failed) return <div className="photo-placeholder" role="img" aria-label={`${media.originalFileName}: ошибка загрузки`}>Ошибка</div>
  if (!objectUrl) return <div className="photo-placeholder" role="status">Загрузка…</div>
  return <img src={objectUrl} alt={media.originalFileName} />
}

function money(value: number, currency: string) { return new Intl.NumberFormat('ru-RU', { style: 'currency', currency }).format(value) }
function qualityStatusLabel(status: string) { return ({ Draft: 'Ожидает проверки', Passed: 'Проверка пройдена', ReworkRequired: 'Нужна доработка', Cancelled: 'Отменено' } as Record<string, string>)[status] ?? status }
function listingStatusLabel(status: string) { return ({ Draft: 'Черновик', Ready: 'Готово к публикации', Published: 'Опубликовано', Unpublished: 'Снято с публикации' } as Record<string, string>)[status] ?? status }
function publicationStatusLabel(status: string) { return ({ Published: 'Опубликовано', Unpublished: 'Снято с публикации', Failed: 'Ошибка публикации' } as Record<string, string>)[status] ?? status }
function mediaCategoryLabel(category: string) { return ({ MainView: 'Экстерьер', Exterior: 'Экстерьер', Interior: 'Интерьер', Defect: 'История повреждений', DamageHistory: 'История повреждений', Documents: 'Внутренние документы', DocumentsInternal: 'Внутренние документы' } as Record<string, string>)[category] ?? category }
function errorText(error: unknown) { return error instanceof ApiError ? error.message : 'Не удалось выполнить операцию.' }
