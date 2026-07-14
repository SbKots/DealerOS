import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { api, apiBlob, apiForm, ApiError } from './api'

export type QueueVehicle = {
  id: string; branchId: string; branchName: string; vin: string; make: string; model: string
  year: number; mileageKm: number; acceptedAt: string
}
type InspectionItem = {
  id: string; key: string; category: string; label: string; description?: string; isRequired: boolean
  sortOrder: number; result: 'Pending' | 'Pass' | 'Fail' | 'NotApplicable'; comment?: string
}
type InspectionPhoto = {
  id: string; sourcePhotoId?: string; originalFileName: string; contentType: string; sizeBytes: number; downloadUrl: string
}
type InspectionDefect = {
  id: string; category: string; title: string; description: string; severity: 'Minor' | 'Major' | 'Critical'
  recommendation?: string; estimatedRepairAmount?: number; currency?: string; repairRequired: boolean
  blocksPublication: boolean; blocksTestDrive: boolean; blocksSale: boolean; photos: InspectionPhoto[]
}
export type InspectionDetail = {
  id: string; vehicleId: string; branchId: string; branchName: string; inspectorId: string; inspectorName: string
  status: 'Draft' | 'InProgress' | 'Completed' | 'Cancelled'; startedAt?: string; completedAt?: string
  mileageKm: number; finalComment?: string; templateName: string; templateVersion: number; revision: number
  correctsInspectionId?: string; needsReconditioning: boolean; version: number; items: InspectionItem[]
  defects: InspectionDefect[]
}
type InspectionSummary = {
  id: string; vehicleId: string; status: InspectionDetail['status']; completedAt?: string; templateName: string
  templateVersion: number; revision: number; needsReconditioning: boolean; defectCount: number
}

const categories = [
  ['Body', 'Кузов'], ['Interior', 'Салон'], ['Engine', 'Двигатель'], ['Transmission', 'Трансмиссия'],
  ['Suspension', 'Подвеска'], ['Brakes', 'Тормоза'], ['Steering', 'Рулевое'], ['Electrical', 'Электрика'],
  ['WheelsAndTires', 'Колёса и шины'], ['DocumentsAndEquipment', 'Документы'], ['TestDrive', 'Тест-драйв'],
] as const

function problem(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 409) return 'Конфликт версий: осмотр изменён в другом окне. Обновите данные.'
    return error.message
  }
  return 'Операция не выполнена. Повторите попытку.'
}

export function InspectionsWorkspace({ focusVehicle, onClearFocus }: {
  focusVehicle?: { id: string; make: string; model: string; vin: string; mileageKm: number } | null
  onClearFocus: () => void
}) {
  const queryClient = useQueryClient()
  const [activeId, setActiveId] = useState<string | null>(null)
  const [detail, setDetail] = useState<InspectionDetail | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmComplete, setConfirmComplete] = useState(false)
  const [finalComment, setFinalComment] = useState('')

  const queue = useQuery({ queryKey: ['inspection-queue'], queryFn: () => api<QueueVehicle[]>('/api/inspections/queue') })
  const history = useQuery({
    queryKey: ['vehicle-inspections', focusVehicle?.id],
    queryFn: () => api<InspectionSummary[]>(`/api/vehicles/${focusVehicle!.id}/inspections`),
    enabled: Boolean(focusVehicle),
  })
  const inspection = useQuery({
    queryKey: ['inspection', activeId],
    queryFn: () => api<InspectionDetail>(`/api/inspections/${activeId}`),
    enabled: Boolean(activeId),
  })
  useEffect(() => { if (inspection.data) setDetail(inspection.data) }, [inspection.data])

  async function run(action: () => Promise<InspectionDetail>) {
    setBusy(true); setError(null)
    try {
      const next = await action()
      setDetail(next); setActiveId(next.id)
      queryClient.setQueryData(['inspection', next.id], next)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['inspection-queue'] }),
        queryClient.invalidateQueries({ queryKey: ['vehicle-inspections', next.vehicleId] }),
        queryClient.invalidateQueries({ queryKey: ['vehicles'] }),
      ])
      return next
    } catch (caught) { setError(problem(caught)); throw caught } finally { setBusy(false) }
  }

  async function start(vehicle: QueueVehicle | typeof focusVehicle) {
    if (!vehicle) return
    await run(() => api<InspectionDetail>(`/api/vehicles/${vehicle.id}/inspections/start`, {
      method: 'POST', body: { mileageKm: vehicle.mileageKm },
    })).catch(() => undefined)
  }

  const progress = useMemo(() => {
    if (!detail?.items.length) return 0
    return Math.round(detail.items.filter((item) => item.result !== 'Pending').length * 100 / detail.items.length)
  }, [detail])

  if (queue.isLoading || history.isLoading || (activeId && inspection.isLoading && !detail)) {
    return <section className="inspection-loading" aria-live="polite">Загружаем осмотры…</section>
  }

  return <div className="inspection-workspace">
    <div className="page-heading inspection-heading">
      <div><p className="eyebrow">Техническая диагностика</p><h1>Осмотры</h1>
        <p className="muted">Очередь, чек-лист, дефекты и неизменяемый результат.</p></div>
      <div className="metric"><span>Ожидают</span><strong>{queue.data?.length ?? 0}</strong><small>автомобилей в очереди</small></div>
    </div>
    {(error || queue.error || history.error || inspection.error) && <div className="error-banner workspace-error" role="alert">
      {error ?? problem(queue.error ?? history.error ?? inspection.error)}
      {activeId && <button className="inline-action" onClick={() => queryClient.invalidateQueries({ queryKey: ['inspection', activeId] })}>Обновить</button>}
    </div>}

    {focusVehicle && <section className="panel vehicle-inspections-panel">
      <div className="panel-title"><span className="step">V</span><div><h2>Осмотры автомобиля</h2><p>{focusVehicle.make} {focusVehicle.model} · <span className="mono">{focusVehicle.vin}</span></p></div>
        <button className="link-button dark" onClick={onClearFocus}>Вся очередь</button></div>
      {!history.data?.length ? <div className="empty-inline"><span>Осмотров ещё нет.</span><button className="primary compact" onClick={() => start(focusVehicle)} disabled={busy}>Начать осмотр</button></div>
        : <div className="history-list">{history.data.map((entry) => <button key={entry.id} onClick={() => { setActiveId(entry.id); setDetail(null) }}>
          <strong>Ревизия {entry.revision} · {inspectionStatusLabel(entry.status)}</strong><span>{entry.templateName} v{entry.templateVersion} · дефектов: {entry.defectCount}</span>
        </button>)}</div>}
    </section>}

    {!detail && !focusVehicle && <section className="registry inspection-queue">
      <div><p className="eyebrow">Рабочая очередь</p><h2>Автомобили для осмотра</h2></div>
      {!queue.data?.length ? <div className="empty-inline">В доступных филиалах нет автомобилей, принятых на склад и ожидающих осмотра.</div>
        : <div className="queue-grid">{queue.data.map((vehicle) => <article key={vehicle.id} className="queue-card">
          <span className="status draft"><i />Ожидает</span><h3>{vehicle.make} {vehicle.model}</h3><p className="mono">{vehicle.vin}</p>
          <dl><div><dt>Пробег</dt><dd>{vehicle.mileageKm.toLocaleString('ru-RU')} км</dd></div><div><dt>Филиал</dt><dd>{vehicle.branchName}</dd></div></dl>
          <button className="primary" onClick={() => start(vehicle)} disabled={busy}>Начать осмотр</button>
        </article>)}</div>}
    </section>}

    {detail && <InspectionForm detail={detail} progress={progress} busy={busy} confirmComplete={confirmComplete}
      finalComment={finalComment} onFinalComment={setFinalComment} onConfirmComplete={setConfirmComplete}
      onBack={() => { setDetail(null); setActiveId(null); setConfirmComplete(false) }}
      onSaveItem={(item, result, comment) => run(() => api<InspectionDetail>(`/api/inspections/${detail.id}/items/${item.id}`, {
        method: 'PUT', body: { result, comment, expectedVersion: detail.version },
      })).catch(() => undefined)}
      onAddDefect={(payload) => run(() => api<InspectionDetail>(`/api/inspections/${detail.id}/defects`, {
        method: 'POST', body: { ...payload, defectId: crypto.randomUUID(), expectedVersion: detail.version },
      })).catch(() => undefined)}
      onPhoto={(defectId, file) => {
        const body = new FormData(); body.append('file', file); body.append('photoId', crypto.randomUUID()); body.append('expectedVersion', String(detail.version))
        return run(() => apiForm<InspectionDetail>(`/api/inspections/${detail.id}/defects/${defectId}/photos`, body)).then(() => undefined).catch(() => undefined)
      }}
      onComplete={() => run(() => api<InspectionDetail>(`/api/inspections/${detail.id}/complete`, {
        method: 'POST', body: { finalComment, expectedVersion: detail.version },
      })).then(() => setConfirmComplete(false)).catch(() => undefined)} />}
  </div>
}

function InspectionForm({ detail, progress, busy, confirmComplete, finalComment, onFinalComment, onConfirmComplete,
  onBack, onSaveItem, onAddDefect, onPhoto, onComplete }: {
  detail: InspectionDetail; progress: number; busy: boolean; confirmComplete: boolean; finalComment: string
  onFinalComment: (value: string) => void; onConfirmComplete: (value: boolean) => void; onBack: () => void
  onSaveItem: (item: InspectionItem, result: InspectionItem['result'], comment?: string) => void
  onAddDefect: (payload: Record<string, unknown>) => void; onPhoto: (defectId: string, file: File) => Promise<void>
  onComplete: () => void
}) {
  const readOnly = detail.status === 'Completed' || detail.status === 'Cancelled'
  const [itemComments, setItemComments] = useState<Record<string, string>>({})
  const [defect, setDefect] = useState({ category: 'Body', title: '', description: '', severity: 'Minor', recommendation: '', estimatedRepairAmount: '', repairRequired: true, blocksPublication: false, blocksTestDrive: false, blocksSale: false })
  return <section className="inspection-form">
    <div className="inspection-toolbar"><button className="link-button dark" onClick={onBack}>← К очереди</button><span className={`status ${readOnly ? 'success' : 'draft'}`}><i />{inspectionStatusLabel(detail.status)}</span><span className="mono">рев. {detail.revision} · версия {detail.version}</span></div>
    <div className="inspection-summary"><div><p className="eyebrow">{detail.templateName} v{detail.templateVersion}</p><h2>Осмотр автомобиля</h2><p>{detail.branchName} · {detail.mileageKm.toLocaleString('ru-RU')} км · диагност {detail.inspectorName}</p></div>
      <div className="progress-block"><strong>{progress}%</strong><span>чек-листа</span><div><i style={{ width: `${progress}%` }} /></div></div></div>
    {readOnly && <div className={detail.needsReconditioning ? 'critical-banner' : 'success-banner'} role="status"><strong>Результат зафиксирован и доступен только для чтения.</strong><span>{detail.needsReconditioning ? 'Следующее действие: открыть раздел «Подготовка» и создать план работ.' : 'Автомобиль готов к следующему этапу.'}</span></div>}
    <div className="inspection-columns">
      <div className="checklist"><h2>Технический чек-лист</h2>{detail.items.map((item, index) => <article key={item.id} className={`check-item ${item.result !== 'Pending' ? 'done' : ''}`}>
        <div className="check-number">{String(index + 1).padStart(2, '0')}</div><div className="check-content"><div><strong>{item.label}{item.isRequired && <em>*</em>}</strong><span>{item.description}</span></div>
          <div className="result-buttons" role="group" aria-label={`Результат: ${item.label}`}>{([['Pass', 'Норма'], ['Fail', 'Дефект'], ['NotApplicable', 'Н/П']] as const).map(([value, label]) => <button key={value} className={item.result === value ? 'selected' : ''} disabled={readOnly || busy} onClick={() => onSaveItem(item, value, itemComments[item.id])}>{label}</button>)}</div>
          {!readOnly && <input aria-label={`Комментарий: ${item.label}`} placeholder="Комментарий к пункту" value={itemComments[item.id] ?? item.comment ?? ''} onChange={(event) => setItemComments((state) => ({ ...state, [item.id]: event.target.value }))} />}
        </div></article>)}</div>
      <aside className="defects-panel"><h2>Дефекты <span>{detail.defects.length}</span></h2>
        {detail.defects.map((entry) => <article key={entry.id} className={`defect-card ${entry.severity.toLowerCase()}`}><div><span className="severity">{severityLabel(entry.severity)}</span><strong>{entry.title}</strong></div><p>{entry.description}</p>
          <div className="block-tags">{entry.repairRequired && <span>Требует ремонта</span>}{entry.blocksSale && <span>Продажа заблокирована</span>}{entry.blocksTestDrive && <span>Без тест-драйва</span>}</div>
          <div className="photos">{entry.photos.map((photo) => <AuthorizedPhoto key={photo.id} photo={photo} />)}{!readOnly && <label className="photo-upload">+ Фото<input aria-label={`Фото: ${entry.title}`} type="file" accept="image/jpeg,image/png,image/webp" disabled={busy} onChange={(event) => { const file = event.target.files?.[0]; if (file) onPhoto(entry.id, file) }} /></label>}</div>
        </article>)}
        {!readOnly && <form className="defect-form" onSubmit={(event) => { event.preventDefault(); onAddDefect({ ...defect, estimatedRepairAmount: defect.estimatedRepairAmount ? Number(defect.estimatedRepairAmount) : null, currency: defect.estimatedRepairAmount ? 'RUB' : null }); setDefect((state) => ({ ...state, title: '', description: '' })) }}>
          <h3>Новый дефект</h3><label>Категория<select aria-label="Категория дефекта" value={defect.category} onChange={(event) => setDefect({ ...defect, category: event.target.value })}>{categories.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
          <label>Название<input aria-label="Название дефекта" required maxLength={160} value={defect.title} onChange={(event) => setDefect({ ...defect, title: event.target.value })} /></label>
          <label>Описание<textarea aria-label="Описание дефекта" required maxLength={2000} value={defect.description} onChange={(event) => setDefect({ ...defect, description: event.target.value })} /></label>
          <label>Серьёзность<select aria-label="Серьёзность" value={defect.severity} onChange={(event) => setDefect({ ...defect, severity: event.target.value })}><option value="Minor">Незначительный</option><option value="Major">Серьёзный</option><option value="Critical">Критический</option></select></label>
          <label>Оценка ремонта, ₽<input aria-label="Оценка ремонта" type="number" min="0" value={defect.estimatedRepairAmount} onChange={(event) => setDefect({ ...defect, estimatedRepairAmount: event.target.value })} /></label>
          <div className="defect-checks"><label><input type="checkbox" checked={defect.repairRequired} onChange={(event) => setDefect({ ...defect, repairRequired: event.target.checked })} />Требует устранения</label><label><input type="checkbox" checked={defect.blocksSale} onChange={(event) => setDefect({ ...defect, blocksSale: event.target.checked })} />Блокирует продажу</label></div>
          {defect.severity === 'Critical' && <div className="critical-note">Сервер принудительно включит ремонт и блокировку продажи.</div>}
          <button className="primary" disabled={busy}>Добавить дефект</button>
        </form>}
      </aside>
    </div>
    {!readOnly && <footer className="inspection-footer">{confirmComplete ? <div className="complete-confirm" role="dialog" aria-label="Подтверждение завершения"><div><strong>Завершить осмотр?</strong><span>После завершения обычное редактирование будет запрещено.</span></div><textarea aria-label="Итоговый комментарий" placeholder="Итоговый комментарий" value={finalComment} onChange={(event) => onFinalComment(event.target.value)} /><div><button className="link-button dark" onClick={() => onConfirmComplete(false)}>Отмена</button><button className="primary compact" onClick={onComplete} disabled={busy}>Подтвердить и завершить</button></div></div>
      : <><span>Заполнено {detail.items.filter((item) => item.result !== 'Pending').length} из {detail.items.length}</span><button className="primary compact" onClick={() => onConfirmComplete(true)} disabled={busy || detail.items.some((item) => item.isRequired && item.result === 'Pending')}>Завершить осмотр</button></>}</footer>}
  </section>
}

function AuthorizedPhoto({ photo }: { photo: InspectionPhoto }) {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    let objectUrl: string | null = null
    apiBlob(photo.downloadUrl).then((blob) => { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl) }).catch(() => setUrl(null))
    return () => { if (objectUrl) URL.revokeObjectURL(objectUrl) }
  }, [photo.downloadUrl])
  return url ? <a href={url} target="_blank" rel="noreferrer"><img src={url} alt={photo.originalFileName} /></a> : <span className="photo-placeholder">Фото</span>
}

function inspectionStatusLabel(status: string) { return ({ Draft: 'Черновик', InProgress: 'В работе', Completed: 'Завершён', Cancelled: 'Отменён' } as Record<string, string>)[status] ?? status }
function severityLabel(severity: string) { return ({ Minor: 'Незначительный', Major: 'Серьёзный', Critical: 'Критический' } as Record<string, string>)[severity] ?? severity }
