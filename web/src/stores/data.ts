import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api, ApiError } from '@/api'
import { duplicateKey, placeText } from '@/format'
import type { Container, FilamentData, Item, Printer, Spool, SpoolPlace } from '@/types'

/** Ohne Angaben gilt dieser Container; "Lose" zeigt auch Lager-Spulen ohne Container (wie in den Apps). */
export const LOOSE_ID = 'standard_lose'
/** Alter Standard-Container für Drucker; jeder Drucker hat heute einen eigenen. */
export const PRINTER_STANDARD_ID = 'standard_drucker'

export interface ItemInput {
  name?: string
  description?: string
  itemData: FilamentData | null
  netWeight: number | null
  spoolWeight: number | null
  spoolsInStock: number | null
  transmissionDistance: number | null
}

/** Sorten, Spulen, Container und Drucker; alle Ansichten teilen denselben Stand. */
export const useDataStore = defineStore('data', () => {
  const items = ref<Item[]>([])
  const spools = ref<Spool[]>([])
  const containers = ref<Container[]>([])
  const printers = ref<Printer[]>([])
  const loaded = ref(false)
  const loading = ref(false)
  const error = ref<string | null>(null)
  /** Der API-Schlüssel fehlt oder ist falsch. */
  const unauthorized = ref(false)

  async function refresh() {
    loading.value = true
    try {
      const [i, s, c, p] = await Promise.all([
        api.get<Item[]>('/items'),
        api.get<Spool[]>('/spools'),
        api.get<Container[]>('/containers'),
        api.get<Printer[]>('/printers'),
      ])
      items.value = i
      spools.value = s
      containers.value = c
      printers.value = p
      loaded.value = true
      error.value = null
      unauthorized.value = false
    } catch (e) {
      error.value = (e as Error).message
      unauthorized.value = e instanceof ApiError && e.status === 401
    } finally {
      loading.value = false
    }
  }

  const itemById = computed(() => new Map(items.value.map((i) => [i.id, i])))
  const printerById = computed(() => new Map(printers.value.map((p) => [p.id, p])))

  const item = (id: string | null | undefined) => (id ? itemById.value.get(id) : undefined)
  const printer = (id: string | null | undefined) => (id ? printerById.value.get(id) : undefined)
  const container = (id: string | null | undefined) => containers.value.find((c) => c.id === id)

  const spoolsOf = (itemId: string) => spools.value.filter((s) => s.itemId === itemId)

  /** Drucker, dem dieser Container gehört. */
  const printerOwning = (containerId: string) => printers.value.find((p) => p.containerId === containerId)

  /** Container, in die man Spulen legen kann: ohne Drucker-Container. */
  const storageContainers = computed(() =>
    containers.value.filter((c) => c.id !== PRINTER_STANDARD_ID && !printers.value.some((p) => p.containerId === c.id)),
  )

  /** Lager-Spulen in diesem Container; "Lose" zeigt auch die ohne (bekannten) Container. */
  function spoolsInContainer(containerId: string) {
    const known = new Set(containers.value.map((c) => c.id))
    return spools.value.filter(
      (s) =>
        !s.printerId &&
        (s.containerId === containerId || (containerId === LOOSE_ID && (!s.containerId || !known.has(s.containerId)))),
    )
  }

  const spoolInPrinter = (printerId: string, tool: number) =>
    spools.value.find((s) => s.printerId === printerId && (s.tool ?? 0) === tool)

  function possibleDuplicates(it: Item) {
    const key = duplicateKey(it)
    return key ? items.value.filter((other) => other.id !== it.id && duplicateKey(other) === key) : []
  }

  function placeOf(spool: Spool): SpoolPlace {
    return spool.printerId
      ? { printerId: spool.printerId, tool: spool.tool ?? 0, containerId: null }
      : { printerId: null, tool: null, containerId: spool.containerId }
  }

  const placeLabel = (place: SpoolPlace) => placeText(place, printers.value, containers.value)

  // Änderungen gehen ans Backend; danach wird der betroffene Teil neu geladen, weil das Backend
  // dabei oft mehr ändert (Vorrat beim Anbrechen, verdrängte Spule beim Einlegen …)

  async function createItem(input: ItemInput & { name: string }) {
    const created = await api.post<Item>('/items', { rfidTag: '', containerId: null, ...input })
    items.value.push(created)
    return created
  }

  async function updateItem(id: string, input: ItemInput & { name: string; description: string }) {
    const updated = await api.put<Item>(`/items/${id}`, input)
    items.value = items.value.map((i) => (i.id === id ? updated : i))
    return updated
  }

  async function deleteItem(id: string) {
    await api.delete(`/items/${id}`)
    await refresh()
  }

  async function mergeItem(targetId: string, fromId: string) {
    const result = await api.post<{ item: Item; renumbered: { oldNumber: number; newNumber: number }[] }>(
      `/items/${targetId}/merge`,
      { fromId },
    )
    await refresh()
    return result
  }

  async function openSpool(itemId: string, place: SpoolPlace, remainingWeight: number | null = null) {
    const spool = await api.post<Spool>(`/items/${itemId}/spools`, { remainingWeight, ...place })
    await refresh()
    return spool
  }

  async function updateSpool(id: string, remainingWeight: number | null, place: SpoolPlace) {
    await api.put<Spool>(`/spools/${id}`, { remainingWeight, ...place })
    await refresh()
  }

  async function markSpoolEmpty(id: string) {
    await api.post<Spool>(`/spools/${id}/empty`)
    await refresh()
  }

  async function saveContainer(id: string | null, input: { name: string; description: string; capacity: number | null; rfidTag?: string }) {
    if (id) await api.put<Container>(`/containers/${id}`, input)
    else
      await api.post<Container>('/containers', {
        ...input,
        // Ein Container braucht ein Tag; ohne echtes Tag eins, das kein NFC-Chip haben kann
        rfidTag: input.rfidTag?.trim() || `WEB_${crypto.randomUUID()}`,
      })
    await refresh()
  }

  async function deleteContainer(id: string) {
    await api.delete(`/containers/${id}`)
    await refresh()
  }

  async function savePrinter(
    id: string | null,
    input: { name: string; type: Printer['type']; baseUrl: string; apiKey: string | null; enabled: boolean },
  ) {
    if (id) await api.put<Printer>(`/printers/${id}`, input)
    else await api.post<Printer>('/printers', { ...input, rfidTag: null })
    await refresh()
  }

  async function deletePrinter(id: string) {
    await api.delete(`/printers/${id}`)
    await refresh()
  }

  async function syncPrinter(id: string) {
    const result = await api.post<{ recorded: number; error: string | null }>(`/printers/${id}/sync`)
    await refresh()
    return result
  }

  return {
    items,
    spools,
    containers,
    printers,
    loaded,
    loading,
    error,
    unauthorized,
    refresh,
    item,
    printer,
    container,
    spoolsOf,
    printerOwning,
    storageContainers,
    spoolsInContainer,
    spoolInPrinter,
    possibleDuplicates,
    placeOf,
    placeLabel,
    createItem,
    updateItem,
    deleteItem,
    mergeItem,
    openSpool,
    updateSpool,
    markSpoolEmpty,
    saveContainer,
    deleteContainer,
    savePrinter,
    deletePrinter,
    syncPrinter,
  }
})
