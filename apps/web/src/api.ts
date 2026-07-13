const sessionKey = 'dealeros.session'
export const sessionExpiredEvent = 'dealeros:session-expired'

type ApiOptions = { method?: string; body?: unknown; authenticated?: boolean }
export type Session = { accessToken: string; displayName: string; email: string; organizationName: string; branchName: string }

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
  return response.json() as Promise<T>
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
