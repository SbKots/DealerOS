import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api, apiBlob, apiFormWithProgress } from './api'
import { VehiclePhotoGallery, type VehiclePhoto } from './VehiclePhotoGallery'

vi.mock('./api', async (importOriginal) => {
  const original = await importOriginal<typeof import('./api')>()
  return { ...original, api: vi.fn(), apiBlob: vi.fn(), apiFormWithProgress: vi.fn() }
})

const first: VehiclePhoto = {
  id: '10000000-0000-4000-8000-000000000001', vehicleId: '20000000-0000-4000-8000-000000000001',
  category: 'MainView', originalFileName: 'front.jpg', contentType: 'image/jpeg', sizeBytes: 120_000,
  width: 1600, height: 1200, sortOrder: 10, isCover: false, isIncludedInListing: true,
  caption: 'Передний ракурс', focalPointX: 0.5, focalPointY: 0.5, version: 1,
  thumbnailUrl: '/photo/1/thumb', mediumUrl: '/photo/1/medium', largeUrl: '/photo/1/large',
  originalUrl: '/photo/1/original', downloadUrl: '/photo/1/medium', createdAt: '2026-07-14T10:00:00Z',
}
const second: VehiclePhoto = { ...first, id: '10000000-0000-4000-8000-000000000002', category: 'Rear', originalFileName: 'rear.jpg', caption: 'Задний ракурс', sortOrder: 20, thumbnailUrl: '/photo/2/thumb', largeUrl: '/photo/2/large' }

function renderGallery() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><VehiclePhotoGallery vehicleId={first.vehicleId} canUpload canManage /></QueryClientProvider>)
}

beforeEach(() => {
  vi.mocked(api).mockImplementation(async (path, options) => {
    if (path.endsWith('/photos') && (!options?.method || options.method === 'GET')) return [first, second] as never
    if (path.endsWith('/photos/inspection-sources')) return [] as never
    return first as never
  })
  vi.mocked(apiBlob).mockResolvedValue(new Blob(['photo'], { type: 'image/jpeg' }))
  vi.mocked(apiFormWithProgress).mockImplementation(async (_path, _body, progress) => { progress(55); progress(100); return first as never })
  vi.stubGlobal('confirm', vi.fn(() => true))
  vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => `blob:${crypto.randomUUID()}`), revokeObjectURL: vi.fn() }))
})

afterEach(() => { vi.clearAllMocks(); vi.unstubAllGlobals() })

describe('VehiclePhotoGallery', () => {
  it('opens the lightbox, manages cover and persists drag reordering', async () => {
    renderGallery()
    await screen.findByText('Передний ракурс')
    await userEvent.click(screen.getByRole('button', { name: 'Открыть front.jpg' }))
    expect(screen.getByRole('dialog', { name: 'Передний ракурс' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Следующее фото' }))
    expect(screen.getByRole('dialog', { name: 'Задний ракурс' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Закрыть' }))

    await userEvent.click(screen.getAllByRole('button', { name: 'Обложка' })[0])
    await waitFor(() => expect(vi.mocked(api)).toHaveBeenCalledWith(expect.stringContaining('/cover'), expect.objectContaining({ method: 'POST' })))

    const cards = document.querySelectorAll('.vehicle-photo-grid > article')
    fireEvent.dragStart(cards[0])
    fireEvent.dragOver(cards[1])
    fireEvent.drop(cards[1])
    await waitFor(() => expect(vi.mocked(api)).toHaveBeenCalledWith(expect.stringContaining('/reorder'), expect.objectContaining({ method: 'POST' })))
  })

  it('uploads multiple files with progress, rejects HEIC locally and allows retryable errors', async () => {
    vi.mocked(apiFormWithProgress).mockRejectedValueOnce(new Error('Сеть недоступна')).mockImplementationOnce(async (_path, _body, progress) => { progress(100); return first as never })
    renderGallery()
    await screen.findByText('Передний ракурс')
    const input = screen.getByLabelText('Выбрать фотографии')
    await userEvent.upload(input, [new File(['valid'], 'new.webp', { type: 'image/webp' }), new File(['heic'], 'phone.heic', { type: 'image/heic' })], { applyAccept: false })

    expect(await screen.findByText(/HEIC\/HEIF пока не поддерживается/)).toBeInTheDocument()
    expect(await screen.findByText('Сеть недоступна')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }))
    expect(await screen.findByText('Загружено')).toBeInTheDocument()
    expect(vi.mocked(apiFormWithProgress)).toHaveBeenCalledTimes(2)
  })

  it('keeps management controls hidden for a read-only employee', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={client}><VehiclePhotoGallery vehicleId={first.vehicleId} canUpload={false} canManage={false} /></QueryClientProvider>)
    await screen.findByText('Передний ракурс')
    expect(screen.queryByLabelText('Выбрать фотографии')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Обложка' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Открыть front.jpg' })).toBeInTheDocument()
  })
})
