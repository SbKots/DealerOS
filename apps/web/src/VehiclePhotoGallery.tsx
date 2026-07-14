import { useEffect, useMemo, useRef, useState, type DragEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiBlob, apiFormWithProgress, ApiError } from './api'
import { EmptyState, Icon, LoadingState, VehicleVisual } from './ui'

export type VehiclePhoto = {
  id: string; vehicleId: string; category?: string; originalFileName: string; contentType: string
  sizeBytes: number; width: number; height: number; sortOrder: number; isCover: boolean
  isIncludedInListing: boolean; caption?: string; focalPointX?: number; focalPointY?: number
  sourceInspectionPhotoId?: string; version: number; thumbnailUrl: string; mediumUrl: string
  largeUrl: string; originalUrl: string; downloadUrl: string; createdAt: string
}

type InspectionSource = {
  photoId: string; inspectionId: string; defectId: string; defectTitle: string
  originalFileName: string; createdAt: string; previewUrl: string
}

type UploadItem = {
  id: string; file: File; previewUrl: string; progress: number
  status: 'queued' | 'uploading' | 'complete' | 'error'; error?: string
}

const maxBytes = 8 * 1024 * 1024
const acceptedTypes = new Set(['image/jpeg', 'image/png', 'image/webp'])
const categories = [
  ['MainView', 'Основной вид'], ['Front', 'Спереди'], ['Rear', 'Сзади'], ['LeftSide', 'Левая сторона'],
  ['RightSide', 'Правая сторона'], ['Interior', 'Салон'], ['Dashboard', 'Панель приборов'],
  ['Trunk', 'Багажник'], ['Engine', 'Двигатель'], ['Defect', 'Дефект'], ['Documents', 'Документы'], ['Other', 'Другое'],
] as const

export function VehiclePhotoGallery({ vehicleId, canUpload, canManage }: { vehicleId: string; canUpload: boolean; canManage: boolean }) {
  const client = useQueryClient()
  const queryKey = ['vehicle-photos', vehicleId]
  const photos = useQuery({ queryKey, queryFn: () => api<VehiclePhoto[]>(`/api/vehicles/${vehicleId}/photos`) })
  const ordered = useMemo(() => [...(photos.data ?? [])].sort((a, b) => a.sortOrder - b.sortOrder || a.createdAt.localeCompare(b.createdAt)), [photos.data])
  const [uploads, setUploads] = useState<UploadItem[]>([])
  const uploadsRef = useRef<UploadItem[]>([])
  uploadsRef.current = uploads
  const [uploadCategory, setUploadCategory] = useState('MainView')
  const [dragActive, setDragActive] = useState(false)
  const [draggedPhotoId, setDraggedPhotoId] = useState<string>()
  const [lightboxId, setLightboxId] = useState<string>()
  const [editId, setEditId] = useState<string>()
  const [edit, setEdit] = useState({ category: 'MainView', caption: '', focalPointX: 0.5, focalPointY: 0.5 })
  const [showInspection, setShowInspection] = useState(false)
  const [actionError, setActionError] = useState('')
  const sources = useQuery({ queryKey: ['vehicle-inspection-photo-sources', vehicleId], queryFn: () => api<InspectionSource[]>(`/api/vehicles/${vehicleId}/photos/inspection-sources`), enabled: showInspection })

  useEffect(() => () => uploadsRef.current.forEach((item) => URL.revokeObjectURL(item.previewUrl)), [])
  useEffect(() => {
    if (!lightboxId) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setLightboxId(undefined)
      if (event.key === 'ArrowLeft') stepLightbox(-1)
      if (event.key === 'ArrowRight') stepLightbox(1)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  const refresh = async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey }),
      client.invalidateQueries({ queryKey: ['vehicles'] }),
      client.invalidateQueries({ queryKey: ['vehicle-media', vehicleId] }),
    ])
  }

  const updateUpload = (id: string, next: Partial<UploadItem>) => setUploads((current) => current.map((item) => item.id === id ? { ...item, ...next } : item))

  const upload = async (item: UploadItem) => {
    updateUpload(item.id, { status: 'uploading', progress: 0, error: undefined })
    const body = new FormData()
    body.append('mediaId', item.id)
    body.append('category', uploadCategory)
    body.append('file', item.file)
    try {
      await apiFormWithProgress<VehiclePhoto>(`/api/vehicles/${vehicleId}/photos`, body, (progress) => updateUpload(item.id, { progress }))
      updateUpload(item.id, { status: 'complete', progress: 100 })
      await refresh()
    } catch (error) {
      updateUpload(item.id, { status: 'error', error: errorText(error) })
    }
  }

  const enqueue = (files: FileList | File[]) => {
    const next = Array.from(files).map((file) => {
      const id = crypto.randomUUID()
      const invalid = !acceptedTypes.has(file.type)
        ? 'Поддерживаются только JPEG, PNG и WebP. HEIC/HEIF пока не поддерживается.'
        : file.size > maxBytes ? 'Файл больше допустимых 8 МБ.' : undefined
      return { id, file, previewUrl: URL.createObjectURL(file), progress: 0, status: invalid ? 'error' : 'queued', error: invalid } as UploadItem
    })
    setUploads((current) => [...current, ...next])
    void next.filter((item) => item.status === 'queued').reduce(
      (previous, item) => previous.then(() => upload(item)), Promise.resolve())
  }

  const command = async (work: () => Promise<unknown>) => {
    setActionError('')
    try { await work(); await refresh() } catch (error) { setActionError(errorText(error)) }
  }

  const setCover = (photo: VehiclePhoto) => command(() => api(`/api/vehicles/${vehicleId}/photos/${photo.id}/cover`, { method: 'POST', body: { expectedVersion: photo.version } }))
  const setListingSelection = (photo: VehiclePhoto) => command(() => api(`/api/vehicles/${vehicleId}/photos/${photo.id}/listing-selection`, { method: 'POST', body: { isIncluded: !photo.isIncludedInListing, expectedVersion: photo.version } }))
  const remove = (photo: VehiclePhoto) => {
    if (!window.confirm(`Удалить «${photo.originalFileName}» из галереи?`)) return
    void command(() => api(`/api/vehicles/${vehicleId}/photos/${photo.id}?expectedVersion=${photo.version}`, { method: 'DELETE' }))
  }
  const beginEdit = (photo: VehiclePhoto) => {
    setEditId(photo.id)
    setEdit({ category: photo.category ?? 'MainView', caption: photo.caption ?? '', focalPointX: photo.focalPointX ?? 0.5, focalPointY: photo.focalPointY ?? 0.5 })
  }
  const saveEdit = (photo: VehiclePhoto) => command(async () => {
    await api(`/api/vehicles/${vehicleId}/photos/${photo.id}`, { method: 'PUT', body: { ...edit, caption: edit.caption || null, expectedVersion: photo.version } })
    setEditId(undefined)
  })
  const reorder = (targetId: string) => {
    if (!draggedPhotoId || draggedPhotoId === targetId) return
    const next = [...ordered]
    const from = next.findIndex((item) => item.id === draggedPhotoId)
    const to = next.findIndex((item) => item.id === targetId)
    next.splice(to, 0, next.splice(from, 1)[0])
    setDraggedPhotoId(undefined)
    void command(() => api(`/api/vehicles/${vehicleId}/photos/reorder`, { method: 'POST', body: { items: next.map((item, index) => ({ mediaId: item.id, sortOrder: (index + 1) * 10, expectedVersion: item.version })) } }))
  }
  const stepLightbox = (offset: number) => {
    if (!lightboxId || !ordered.length) return
    const index = ordered.findIndex((item) => item.id === lightboxId)
    setLightboxId(ordered[(index + offset + ordered.length) % ordered.length].id)
  }

  const dropFiles = (event: DragEvent) => {
    event.preventDefault(); setDragActive(false)
    if (canUpload && event.dataTransfer.files.length) enqueue(event.dataTransfer.files)
  }
  const lightboxPhoto = ordered.find((photo) => photo.id === lightboxId)

  return <section className="vehicle-gallery" aria-label="Фотогалерея автомобиля">
    <div className="gallery-heading"><div><span className="overline">Медиа автомобиля</span><h3>Фотогалерея</h3><p>{ordered.length} из 100 фотографий · оригиналы доступны только сотрудникам с правом просмотра</p></div>{canUpload && <button className="secondary" onClick={() => setShowInspection((value) => !value)}>{showInspection ? 'Скрыть фото осмотра' : 'Добавить из осмотра'}</button>}</div>
    {actionError && <div className="error-banner" role="alert">{actionError}</div>}
    {canUpload && <div className={`gallery-dropzone ${dragActive ? 'active' : ''}`} onDragEnter={(event) => { event.preventDefault(); setDragActive(true) }} onDragOver={(event) => event.preventDefault()} onDragLeave={() => setDragActive(false)} onDrop={dropFiles}>
      <Icon name="car" size={24} /><span><strong>Перетащите фотографии сюда</strong><small>JPEG, PNG или WebP · до 8 МБ · сервер проверит реальное содержимое</small></span><label className="secondary">Выбрать файлы<input aria-label="Выбрать фотографии" type="file" multiple accept="image/jpeg,image/png,image/webp" onChange={(event) => event.target.files && enqueue(event.target.files)} /></label><label>Категория<select aria-label="Категория новых фотографий" value={uploadCategory} onChange={(event) => setUploadCategory(event.target.value)}>{categories.map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select></label>
    </div>}
    {uploads.length > 0 && <div className="upload-queue" aria-label="Очередь загрузки">{uploads.map((item) => <article key={item.id} className={item.status}><img src={item.previewUrl} alt="Предпросмотр" /><span><strong>{item.file.name}</strong><small>{item.status === 'error' ? item.error : item.status === 'complete' ? 'Загружено' : `Загрузка ${item.progress}%`}</small><i><b style={{ width: `${item.progress}%` }} /></i></span>{item.status === 'error' && acceptedTypes.has(item.file.type) && item.file.size <= maxBytes && <button className="link-button" onClick={() => void upload(item)}>Повторить</button>}<button className="upload-dismiss" aria-label={`Убрать ${item.file.name} из очереди`} onClick={() => { URL.revokeObjectURL(item.previewUrl); setUploads((current) => current.filter((entry) => entry.id !== item.id)) }}>×</button></article>)}</div>}
    {showInspection && <div className="inspection-photo-picker"><div><strong>Фото завершённых осмотров</strong><small>Импорт создаёт ссылку на неизменяемый оригинал, не копируя бинарный файл.</small></div>{sources.isLoading ? <LoadingState /> : sources.data?.length ? <div className="inspection-source-grid">{sources.data.map((source) => <article key={source.photoId}><AuthenticatedPhoto url={source.previewUrl} alt={source.originalFileName} /><span><strong>{source.defectTitle}</strong><small>{source.originalFileName}</small></span><button className="secondary" onClick={() => void command(() => api(`/api/vehicles/${vehicleId}/photos/import-inspection`, { method: 'POST', body: { mediaId: crypto.randomUUID(), inspectionPhotoId: source.photoId, category: 'Defect' } }))}>Добавить</button></article>)}</div> : <p className="muted">Новых фотографий из завершённых осмотров нет.</p>}</div>}
    {photos.isLoading ? <LoadingState label="Загружаем галерею…" /> : photos.error ? <div className="error-banner" role="alert">{errorText(photos.error)}</div> : ordered.length === 0 ? <EmptyState icon="car" title="Фотографий пока нет" description="Добавьте общий вид автомобиля или импортируйте доказательства из завершённого осмотра." /> : <div className="vehicle-photo-grid">{ordered.map((photo) => <article key={photo.id} className={photo.isCover ? 'cover' : ''} draggable={canManage} onDragStart={() => setDraggedPhotoId(photo.id)} onDragOver={(event) => event.preventDefault()} onDrop={() => reorder(photo.id)}>
      <button className="photo-open" onClick={() => setLightboxId(photo.id)} aria-label={`Открыть ${photo.originalFileName}`}><AuthenticatedPhoto url={photo.thumbnailUrl} alt={photo.caption || photo.originalFileName} focalX={photo.focalPointX} focalY={photo.focalPointY} /><span className="photo-badges">{photo.isCover && <b>Обложка</b>}{photo.sourceInspectionPhotoId && <b>Из осмотра</b>}</span></button>
      <div className="photo-card-copy"><strong>{categoryLabel(photo.category)}</strong><small>{photo.caption || photo.originalFileName}</small><em>{photo.width}×{photo.height} · {formatBytes(photo.sizeBytes)}</em></div>
      {canManage && <div className="photo-actions"><button title="Перетащите карточку для изменения порядка" aria-label="Изменить порядок">↕</button>{!photo.isCover && photo.category !== 'Documents' && <button onClick={() => void setCover(photo)}>Обложка</button>}<button onClick={() => beginEdit(photo)}>Изменить</button><button className={photo.isIncludedInListing ? 'selected' : ''} disabled={photo.category === 'Documents'} onClick={() => void setListingSelection(photo)}>{photo.isIncludedInListing ? 'В объявлении' : 'В объявление'}</button><button className="danger" onClick={() => remove(photo)}>Удалить</button></div>}
      {editId === photo.id && <div className="photo-editor"><label>Категория<select value={edit.category} onChange={(event) => setEdit({ ...edit, category: event.target.value })}>{categories.map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select></label><label>Подпись<input maxLength={300} value={edit.caption} onChange={(event) => setEdit({ ...edit, caption: event.target.value })} /></label><div className="focal-controls"><label>Фокус X<input aria-label="Фокус по горизонтали" type="range" min="0" max="1" step="0.01" value={edit.focalPointX} onChange={(event) => setEdit({ ...edit, focalPointX: Number(event.target.value) })} /></label><label>Фокус Y<input aria-label="Фокус по вертикали" type="range" min="0" max="1" step="0.01" value={edit.focalPointY} onChange={(event) => setEdit({ ...edit, focalPointY: Number(event.target.value) })} /></label></div><div><button className="primary" onClick={() => void saveEdit(photo)}>Сохранить</button><button className="link-button" onClick={() => setEditId(undefined)}>Отмена</button></div></div>}
    </article>)}</div>}
    {lightboxPhoto && <div className="gallery-lightbox" role="dialog" aria-modal="true" aria-label={lightboxPhoto.caption || lightboxPhoto.originalFileName} onMouseDown={(event) => { if (event.target === event.currentTarget) setLightboxId(undefined) }}><button className="lightbox-close" aria-label="Закрыть" onClick={() => setLightboxId(undefined)}>×</button>{ordered.length > 1 && <button className="lightbox-prev" aria-label="Предыдущее фото" onClick={() => stepLightbox(-1)}>‹</button>}<div className="lightbox-content"><AuthenticatedPhoto url={lightboxPhoto.largeUrl} alt={lightboxPhoto.caption || lightboxPhoto.originalFileName} contain /><span><strong>{categoryLabel(lightboxPhoto.category)}</strong><small>{lightboxPhoto.caption || lightboxPhoto.originalFileName} · {ordered.findIndex((item) => item.id === lightboxPhoto.id) + 1} из {ordered.length}</small></span></div>{ordered.length > 1 && <button className="lightbox-next" aria-label="Следующее фото" onClick={() => stepLightbox(1)}>›</button>}</div>}
  </section>
}

export function VehicleCover({ vehicleId, photoId, name, compact = false }: { vehicleId: string; photoId?: string; name: string; compact?: boolean }) {
  if (!photoId) return <VehicleVisual name={name} />
  return <div className={compact ? 'vehicle-cover compact' : 'vehicle-visual vehicle-cover'}><AuthenticatedPhoto url={`/api/vehicles/${vehicleId}/photos/${photoId}/content/${compact ? 'thumbnail' : 'medium'}`} alt={name} /></div>
}

export function AuthenticatedPhoto({ url, alt, focalX, focalY, contain = false }: { url: string; alt: string; focalX?: number; focalY?: number; contain?: boolean }) {
  const [objectUrl, setObjectUrl] = useState('')
  const [failed, setFailed] = useState(false)
  useEffect(() => {
    let active = true
    let createdUrl = ''
    setObjectUrl(''); setFailed(false)
    apiBlob(url).then((blob) => {
      if (!active) return
      createdUrl = URL.createObjectURL(blob); setObjectUrl(createdUrl)
    }).catch(() => { if (active) setFailed(true) })
    return () => { active = false; if (createdUrl) URL.revokeObjectURL(createdUrl) }
  }, [url])
  if (failed) return <span className="photo-placeholder" role="img" aria-label={`${alt}: ошибка загрузки`}>Нет фото</span>
  if (!objectUrl) return <span className="photo-placeholder" role="status">Загрузка…</span>
  return <img src={objectUrl} alt={alt} style={{ objectFit: contain ? 'contain' : 'cover', objectPosition: `${(focalX ?? 0.5) * 100}% ${(focalY ?? 0.5) * 100}%` }} />
}

function categoryLabel(category?: string) {
  const match = categories.find(([value]) => value === category)
  const legacy: Record<string, string> = { Exterior: 'Основной вид', DamageHistory: 'Дефект', DocumentsInternal: 'Документы' }
  return match?.[1] ?? (category ? legacy[category] ?? category : 'Без категории')
}

function formatBytes(value: number) { return value >= 1024 * 1024 ? `${(value / 1024 / 1024).toFixed(1)} МБ` : `${Math.ceil(value / 1024)} КБ` }
function errorText(error: unknown) { return error instanceof ApiError ? error.message : error instanceof Error ? error.message : 'Операция не выполнена. Повторите попытку.' }
