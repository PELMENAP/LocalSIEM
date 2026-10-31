const TOKEN_KEY = 'localsiem.token'

let token: string | null = readToken()
let onUnauthorized: (() => void) | null = null

function readToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

export function getToken() {
  return token
}

export function setToken(value: string | null) {
  token = value
  try {
    if (value) localStorage.setItem(TOKEN_KEY, value)
    else localStorage.removeItem(TOKEN_KEY)
  } catch {
    return
  }
}

export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler
}

export class ApiError extends Error {
  status: number
  errors: Record<string, string[]> | null

  constructor(status: number, message: string, errors: Record<string, string[]> | null) {
    super(message)
    this.status = status
    this.errors = errors
  }

  get details(): string {
    if (!this.errors) return this.message
    const lines = Object.values(this.errors).flat()
    return lines.length ? `${this.message}: ${lines.join('; ')}` : this.message
  }
}

type Query = Record<string, string | number | boolean | null | undefined | (string | number)[]>

export function buildQuery(query?: Query): string {
  if (!query) return ''
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    if (Array.isArray(value)) value.forEach((v) => params.append(key, String(v)))
    else params.append(key, String(value))
  }
  const text = params.toString()
  return text ? `?${text}` : ''
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (token) headers.Authorization = `Bearer ${token}`
  let response: Response
  try {
    response = await fetch(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  } catch {
    throw new ApiError(0, 'Сервер недоступен', null)
  }
  if (response.status === 401 && !url.startsWith('/api/auth/login')) {
    onUnauthorized?.()
  }
  if (!response.ok) {
    let message = `Ошибка ${response.status}`
    let errors: Record<string, string[]> | null = null
    try {
      const problem = await response.json()
      message = problem.title ?? message
      errors = problem.errors ?? null
    } catch {
      if (response.status === 429) message = 'Слишком много запросов, попробуйте позже'
      if (response.status === 403) message = 'Недостаточно прав'
    }
    throw new ApiError(response.status, message, errors)
  }
  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(url: string, query?: Query) => request<T>('GET', url + buildQuery(query)),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  put: <T>(url: string, body: unknown) => request<T>('PUT', url, body),
  patch: <T>(url: string, body: unknown) => request<T>('PATCH', url, body),
  del: <T>(url: string) => request<T>('DELETE', url)
}
