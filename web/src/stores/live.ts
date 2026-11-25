import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api, ApiError, readEvents } from '@/api'
import { useDataStore } from '@/stores/data'
import type { PrinterLive } from '@/types'

/**
 * Zustand der Drucker, live über /api/printers/events: der Server schickt ihn bei jeder Änderung (U1 und P1S melden sich
 * selbst, die anderen fragt er alle paar Sekunden). Reißt die Verbindung ab, wird neu verbunden; bis dahin fragen die
 * Ansichten wie früher alle 15 s nach. Ein Backend ohne den Endpunkt (404) bleibt beim Nachfragen.
 */
export const useLiveStore = defineStore('live', () => {
  const printers = ref<PrinterLive[]>([])
  const byId = computed(() => new Map(printers.value.map((l) => [l.printerId, l])))
  /** Die Verbindung steht; dann ist Nachfragen überflüssig. */
  const connected = ref(false)
  const loading = ref(false)

  let controller: AbortController | null = null
  let retry: number | undefined
  let delay = 2_000
  let unsupported = false
  let dataTimer: number | undefined

  /** Einmal nachfragen, z. B. beim Neuladen von Hand oder ohne Verbindung. */
  async function load() {
    if (loading.value) return
    loading.value = true
    try {
      printers.value = await api.get<PrinterLive[]>('/printers/status')
    } catch {
      // Fehler zeigt schon der Hinweis oben
    } finally {
      loading.value = false
    }
  }

  function start() {
    if (controller || unsupported) return
    window.clearTimeout(retry)
    const current = new AbortController()
    controller = current
    readEvents(
      '/printers/events',
      ({ event, data }) => {
        connected.value = true
        delay = 2_000
        if (event === 'status') printers.value = JSON.parse(data) as PrinterLive[]
        else if (event === 'changed') {
          // Ein Abgleich kommt pro Drucker; kurz sammeln, dann einmal neu laden
          window.clearTimeout(dataTimer)
          dataTimer = window.setTimeout(() => useDataStore().refresh(), 500)
        }
      },
      current.signal,
    )
      .catch((e) => {
        if (e instanceof ApiError && e.status === 404) unsupported = true
      })
      .finally(() => {
        if (controller !== current) return
        controller = null
        connected.value = false
        if (current.signal.aborted || unsupported) return
        retry = window.setTimeout(start, delay)
        delay = Math.min(delay * 2, 60_000)
      })
  }

  function stop() {
    window.clearTimeout(retry)
    const current = controller
    controller = null
    connected.value = false
    current?.abort()
  }

  /** Sprache gewechselt: Hinweise kommen vom Server, also neu verbinden. */
  function restart() {
    stop()
    start()
  }

  return { printers, byId, connected, loading, load, start, stop, restart }
})
