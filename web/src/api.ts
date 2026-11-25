import { i18n, t } from './i18n'

// Zugriff auf das Backend. Die Oberfläche läuft auf demselben Server, daher relative Adressen.
// Ist im Backend ein API-Schlüssel gesetzt (Filomat__ApiKey), wird er einmal eingegeben und im Browser gespeichert.

const keyStorage = 'filomat.apiKey'

export function getApiKey(): string {
  try {
    return localStorage.getItem(keyStorage) ?? ''
  } catch {
    return ''
  }
}

export function setApiKey(key: string) {
  try {
    if (key) localStorage.setItem(keyStorage, key)
    else localStorage.removeItem(keyStorage)
  } catch {
    // Ohne Speicher gilt der Schlüssel nur bis zum Neuladen
  }
  sessionKey = key
}

let sessionKey = getApiKey()

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message)
  }
}

/** Fehlertext aus einer Antwort: ValidationProblem, ProblemDetails oder reiner Text. */
async function errorMessage(response: Response): Promise<string> {
  if (response.status === 401) return t('api.keyMissing')
  const text = await response.text()
  try {
    const body = JSON.parse(text)
    if (typeof body === 'string') return body
    if (body?.errors) return Object.values(body.errors as Record<string, string[]>).flat().join(' ')
    if (body?.detail) return body.detail
    if (body?.title) return body.title
  } catch {
    // kein JSON
  }
  return text || t('api.error', { status: response.status })
}

function headers(json: boolean): HeadersInit {
  const result: Record<string, string> = {}
  if (json) result['Content-Type'] = 'application/json'
  if (sessionKey) result['X-Api-Key'] = sessionKey
  // Hinweise und Fehlermeldungen des Backends in der gewählten Sprache
  result['Accept-Language'] = i18n.global.locale.value
  return result
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api${path}`, {
    method,
    headers: headers(body !== undefined),
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) throw new ApiError(await errorMessage(response), response.status)
  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body: unknown = {}) => request<T>('POST', path, body),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  delete: (path: string) => request<void>('DELETE', path),

  /** Bild mit API-Schlüssel laden (ein img-Tag kann keinen Header mitschicken); null, wenn es keins gibt. */
  async blob(path: string): Promise<Blob | null> {
    const response = await fetch(`/api${path}`, { headers: headers(false) })
    return response.ok ? response.blob() : null
  },
}

/** Ereignis aus /api/printers/events. */
export interface ServerEvent {
  event: string
  data: string
}

/**
 * Liest Server-Sent Events. EventSource kann keinen API-Schlüssel mitschicken, daher fetch mit gestreamter Antwort.
 * Endet, wenn der Server die Verbindung schließt oder signal abbricht; wirft bei Fehlern (z. B. 401, Netz weg).
 */
export async function readEvents(path: string, onEvent: (event: ServerEvent) => void, signal: AbortSignal): Promise<void> {
  const response = await fetch(`/api${path}`, { headers: { ...headers(false), Accept: 'text/event-stream' }, signal })
  if (!response.ok || !response.body) throw new ApiError(await errorMessage(response), response.status)
  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader()
  let buffer = ''
  let event = 'message'
  let data: string[] = []
  for (;;) {
    const { value, done } = await reader.read()
    if (done) return
    buffer += value
    let newline: number
    while ((newline = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, newline).replace(/\r$/, '')
      buffer = buffer.slice(newline + 1)
      if (line === '') {
        if (data.length) onEvent({ event, data: data.join('\n') })
        event = 'message'
        data = []
      } else if (line.startsWith('event:')) event = line.slice(6).trim()
      else if (line.startsWith('data:')) data.push(line.slice(5).replace(/^ /, ''))
      // Kommentare (": ping") und retry: nichts zu tun
    }
  }
}

