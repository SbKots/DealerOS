const sessionKey = 'dealeros.session'
export const sessionExpiredEvent = 'dealeros:session-expired'

type ApiOptions = { method?: string; body?: unknown; authenticated?: boolean }
export type Session = { accessToken: string; displayName: string; email: string; organizationName: string; branchName: string; permissions?: string[] }

export class ApiError extends Error {
  status: number
  code?: string
  constructor(message: string, status: number, code?: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

export function hasSession() { return Boolean(sessionStorage.getItem(sessionKey)) }
export function getSession(): Session | null {
  const value = sessionStorage.getItem(sessionKey)
  if (!value) return null
  try {
    return JSON.parse(value) as Session
  } catch {
    sessionStorage.removeItem(sessionKey)
    return null
  }
}
export function saveSession(session: Session) { sessionStorage.setItem(sessionKey, JSON.stringify(session)) }
export function clearSession() { sessionStorage.removeItem(sessionKey) }
function expireSession() {
  clearSession()
  window.dispatchEvent(new Event(sessionExpiredEvent))
}

export async function api<T>(path: string, options: ApiOptions = {}): Promise<T> {
  const token = getSession()?.accessToken
  const response = await fetch(path, {
    method: options.method ?? 'GET',
    headers: {
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(options.authenticated !== false && token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: options.body ? JSON.stringify(options.body) : undefined,
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string; code?: string }
    if (response.status === 401 && options.authenticated !== false) expireSession()
    throw new ApiError(problem.title ?? `Ошибка HTTP ${response.status}`, response.status, problem.code)
  }
  const text = await response.text()
  return (text ? JSON.parse(text) : null) as T
}

export async function apiForm<T>(path: string, body: FormData): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    headers: getSession()?.accessToken ? { Authorization: `Bearer ${getSession()!.accessToken}` } : {},
    body,
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string; code?: string }
    if (response.status === 401) expireSession()
    throw new ApiError(problem.title ?? `Ошибка HTTP ${response.status}`, response.status, problem.code)
  }
  return response.json() as Promise<T>
}

export function apiFormWithProgress<T>(path: string, body: FormData, onProgress: (percent: number) => void): Promise<T> {
  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest()
    request.open('POST', path)
    const token = getSession()?.accessToken
    if (token) request.setRequestHeader('Authorization', `Bearer ${token}`)
    request.upload.addEventListener('progress', (event) => {
      if (event.lengthComputable) onProgress(Math.round((event.loaded / event.total) * 100))
    })
    request.addEventListener('load', () => {
      if (request.status === 401) expireSession()
      if (request.status < 200 || request.status >= 300) {
        let problem: { title?: string; code?: string } = {}
        try { problem = JSON.parse(request.responseText) as typeof problem } catch { /* non-JSON proxy error */ }
        reject(new ApiError(problem.title ?? `Ошибка HTTP ${request.status}`, request.status, problem.code))
        return
      }
      onProgress(100)
      try { resolve((request.responseText ? JSON.parse(request.responseText) : null) as T) }
      catch { reject(new ApiError('Сервер вернул некорректный ответ.', request.status)) }
    })
    request.addEventListener('error', () => reject(new ApiError('Не удалось загрузить файл. Проверьте соединение и повторите попытку.', 0)))
    request.addEventListener('abort', () => reject(new ApiError('Загрузка отменена.', 0)))
    request.send(body)
  })
}

export async function apiBlob(path: string): Promise<Blob> {
  const response = await fetch(path, {
    headers: getSession()?.accessToken ? { Authorization: `Bearer ${getSession()!.accessToken}` } : {},
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string; code?: string }
    if (response.status === 401) expireSession()
    throw new ApiError(problem.title ?? `Ошибка HTTP ${response.status}`, response.status, problem.code)
  }
  return response.blob()
}
