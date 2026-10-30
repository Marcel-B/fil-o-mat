<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api } from '@/api'
import { useDataStore } from '@/stores/data'
import { displayTitle, formatDateTime, grams, printerPlace } from '@/format'
import ColorDot from '@/components/ColorDot.vue'
import InsertSpoolDialog from '@/components/InsertSpoolDialog.vue'
import PrinterCard from '@/components/PrinterCard.vue'
import PrinterDialog from '@/components/PrinterDialog.vue'
import SpoolDialog from '@/components/SpoolDialog.vue'
import type { Consumption, Printer, PrinterLive, Spool, ToolLive } from '@/types'

const store = useDataStore()
const toast = useToast()

const live = ref<PrinterLive[]>([])
const liveById = computed(() => new Map(live.value.map((l) => [l.printerId, l])))
const loadingLive = ref(false)

// Wie die Apps: alle 15 s, solange die Seite sichtbar ist (das Backend fragt die Drucker höchstens alle 10 s)
async function loadLive() {
  if (loadingLive.value || document.visibilityState !== 'visible') return
  loadingLive.value = true
  try {
    live.value = await api.get<PrinterLive[]>('/printers/status')
  } catch {
    // Fehler zeigt schon der Hinweis oben
  } finally {
    loadingLive.value = false
  }
}

const unassigned = ref<Consumption[]>([])
async function loadUnassigned() {
  try {
    unassigned.value = await api.get<Consumption[]>('/consumption?unassigned=true')
  } catch {
    unassigned.value = []
  }
}

let timer: number | undefined
onMounted(() => {
  loadLive()
  loadUnassigned()
  timer = window.setInterval(loadLive, 15_000)
})
onUnmounted(() => window.clearInterval(timer))
// Spule eingelegt oder umgesteckt: Köpfe und Hinweise gleich neu
watch(() => store.spools, loadLive)

async function reloadAll() {
  await Promise.all([store.refresh(), loadLive(), loadUnassigned()])
}

// Drucker anlegen/bearbeiten
const editedPrinter = ref<Printer | null>(null)
const printerDialog = ref(false)
function editPrinter(printer: Printer | null) {
  editedPrinter.value = printer
  printerDialog.value = true
}

// Klick auf einen Kopf: Spule bearbeiten oder eine einlegen
const editedSpool = ref<Spool | null>(null)
const spoolDialog = ref(false)
const insertPrinter = ref<Printer | null>(null)
const insertTool = ref(0)
const insertDialog = ref(false)
function onTool(printer: Printer, tool: ToolLive) {
  const spool = tool.spoolId ? store.spools.find((s) => s.id === tool.spoolId) : undefined
  if (spool) {
    editedSpool.value = spool
    spoolDialog.value = true
  } else {
    insertPrinter.value = printer
    insertTool.value = tool.tool
    insertDialog.value = true
  }
}

// Offene Buchungen zuordnen
const assigning = ref<Consumption | null>(null)
const assignItem = ref<string | null>(null)
const assignSpool = ref<string | null>(null)
const assignDialog = ref(false)
const assignSaving = ref(false)
const itemOptions = computed(() =>
  store.items
    .map((i) => ({ id: i.id, label: `${displayTitle(i)}${i.itemData?.color ? ` · ${i.itemData.color}` : ''}`, color: i.itemData?.color_hex }))
    .sort((a, b) => a.label.localeCompare(b.label, 'de')),
)
const spoolOptions = computed(() =>
  assignItem.value
    ? store.spoolsOf(assignItem.value).map((s) => ({ id: s.id, label: `#${s.number} · ${store.placeLabel(store.placeOf(s))}` }))
    : [],
)
function startAssign(entry: Consumption) {
  assigning.value = entry
  assignItem.value = null
  assignSpool.value = null
  assignDialog.value = true
}
async function assign() {
  if (!assigning.value || !assignItem.value) return
  assignSaving.value = true
  try {
    await api.post(`/consumption/${assigning.value.id}/assign`, { itemId: assignItem.value, spoolId: assignSpool.value })
    assignDialog.value = false
    await Promise.all([store.refresh(), loadUnassigned()])
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Zuordnen ging nicht', detail: (e as Error).message, life: 6000 })
  } finally {
    assignSaving.value = false
  }
}
const toolLabel = (entry: Consumption) => {
  const printer = store.printer(entry.printerId)
  return printer ? printerPlace(printer, entry.tool) : 'Gelöschter Drucker'
}
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-4">
    <div class="flex flex-wrap items-center gap-3">
      <h1 class="text-2xl font-semibold">Drucker</h1>
      <span class="flex-1" />
      <Button icon="pi pi-refresh" text rounded aria-label="Neu laden" :loading="loadingLive" @click="reloadAll" />
      <Button label="Neuer Drucker" icon="pi pi-plus" @click="editPrinter(null)" />
    </div>

    <div v-if="store.loaded && !store.printers.length" class="rounded-xl border border-dashed border-surface-300 p-10 text-center text-muted-color dark:border-surface-700">
      Noch kein Drucker eingerichtet.
    </div>

    <div class="grid gap-4 md:grid-cols-2 2xl:grid-cols-3">
      <PrinterCard
        v-for="printer in store.printers"
        :key="printer.id"
        :printer="printer"
        :live="liveById.get(printer.id)"
        @edit="editPrinter(printer)"
        @tool="onTool(printer, $event)"
        @synced="loadLive(); loadUnassigned()"
      />
    </div>

    <Card v-if="unassigned.length">
      <template #title>Verbrauch ohne Spule ({{ unassigned.length }})</template>
      <template #subtitle>Bei diesen Drucken war nicht klar, von welcher Spule abgebucht wird.</template>
      <template #content>
        <DataTable :value="unassigned" size="small" data-key="id" paginator :rows="10">
          <Column header="Ende"><template #body="{ data }">{{ formatDateTime(data.endedAt) }}</template></Column>
          <Column header="Druck" field="jobName" />
          <Column header="Platz"><template #body="{ data }">{{ toolLabel(data) }}</template></Column>
          <Column header="Menge" class="text-right"><template #body="{ data }">{{ grams(data.grams) }}</template></Column>
          <Column class="w-32 text-right">
            <template #body="{ data }"><Button label="Zuordnen" size="small" outlined @click="startAssign(data)" /></template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <PrinterDialog v-model:visible="printerDialog" :printer="editedPrinter" />
    <SpoolDialog v-model:visible="spoolDialog" :spool="editedSpool" show-item />
    <InsertSpoolDialog v-model:visible="insertDialog" :printer="insertPrinter" :tool="insertTool" />

    <Dialog v-model:visible="assignDialog" modal header="Verbrauch zuordnen" class="w-[30rem] max-w-[95vw]">
      <div v-if="assigning" class="flex flex-col gap-4">
        <p class="text-sm">{{ assigning.jobName }} · {{ toolLabel(assigning) }} · {{ grams(assigning.grams) }}</p>
        <label class="flex flex-col gap-1 text-sm">
          Sorte
          <Select v-model="assignItem" :options="itemOptions" option-label="label" option-value="id" filter fluid @change="assignSpool = null">
            <template #option="{ option }">
              <div class="flex items-center gap-2"><ColorDot :hex="option.color" :size="16" />{{ option.label }}</div>
            </template>
          </Select>
        </label>
        <label v-if="assignItem" class="flex flex-col gap-1 text-sm">
          Spule
          <Select v-model="assignSpool" :options="spoolOptions" option-label="label" option-value="id" show-clear placeholder="automatisch (die am Platz, sonst eine neue)" fluid />
        </label>
      </div>
      <template #footer>
        <Button label="Abbrechen" severity="secondary" text @click="assignDialog = false" />
        <Button label="Abbuchen" icon="pi pi-check" :loading="assignSaving" :disabled="!assignItem" @click="assign" />
      </template>
    </Dialog>
  </div>
</template>
