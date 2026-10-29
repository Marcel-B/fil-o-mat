<script setup lang="ts">
import { computed, ref } from 'vue'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useDataStore } from '@/stores/data'
import { ago, duration, grams } from '@/format'
import ColorDot from './ColorDot.vue'
import PrinterThumbnail from './PrinterThumbnail.vue'
import type { Printer, PrinterLive, PrintState, ToolLive } from '@/types'

const props = defineProps<{ printer: Printer; live: PrinterLive | undefined }>()
const emit = defineEmits<{ edit: []; tool: [tool: ToolLive]; synced: [] }>()
const store = useDataStore()
const toast = useToast()
const confirm = useConfirm()

const states: Record<PrintState, { label: string; severity: string }> = {
  Idle: { label: 'Bereit', severity: 'secondary' },
  Printing: { label: 'Druckt', severity: 'info' },
  Paused: { label: 'Pausiert', severity: 'warn' },
  Finished: { label: 'Fertig', severity: 'success' },
  Cancelled: { label: 'Abgebrochen', severity: 'warn' },
  Error: { label: 'Fehler', severity: 'danger' },
  Offline: { label: 'Nicht erreichbar', severity: 'contrast' },
}

const status = computed(() => props.live?.status)
const state = computed(() => (status.value ? states[status.value.state] : null))
const typeLabel: Record<string, string> = {
  SnapmakerU1: 'Snapmaker U1',
  Moonraker: 'Klipper',
  PrusaLink: 'PrusaLink',
  Bambu: 'Bambu Lab',
  Anycubic: 'Anycubic',
}

// Ohne Zustand (noch nie abgefragt) die Köpfe aus den Spulen der App
const tools = computed<ToolLive[]>(() => {
  if (props.live?.tools.length) return props.live.tools
  return Array.from({ length: props.printer.tools }, (_, tool) => {
    const spool = store.spoolInPrinter(props.printer.id, tool)
    const item = store.item(spool?.itemId)
    return {
      tool,
      place: '',
      spoolId: spool?.id ?? null,
      spoolNumber: spool?.number ?? null,
      itemId: item?.id ?? null,
      itemName: item?.name ?? null,
      type: item?.itemData?.type ?? null,
      colorHex: item?.itemData?.color_hex ?? null,
      remainingGrams: spool?.remainingWeight ?? null,
      reported: null,
      neededGrams: null,
      enough: null,
    }
  })
})

const heaters = computed(() => (status.value?.heaters ?? []).filter((h) => h.actual != null))
const dryerFor = (tool: number) => status.value?.dryers?.find((d) => d.tools.includes(tool))

const syncing = ref(false)
async function sync() {
  syncing.value = true
  try {
    const result = await store.syncPrinter(props.printer.id)
    toast.add(
      result.error
        ? { severity: 'error', summary: 'Abgleich ging nicht', detail: result.error, life: 6000 }
        : { severity: 'success', summary: `${props.printer.name} abgeglichen`, detail: result.recorded ? `${result.recorded} Drucke gebucht` : 'Keine neuen Drucke', life: 3000 },
    )
    emit('synced')
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Abgleich ging nicht', detail: (e as Error).message, life: 6000 })
  } finally {
    syncing.value = false
  }
}

function remove() {
  confirm.require({
    header: 'Drucker entfernen',
    message: `„${props.printer.name}“ entfernen? Eingelegte Spulen wandern ins Lager, der Verbrauch bleibt.`,
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: 'Entfernen', severity: 'danger' },
    rejectProps: { label: 'Abbrechen', severity: 'secondary', outlined: true },
    accept: () => store.deletePrinter(props.printer.id).catch((e) => toast.add({ severity: 'error', summary: 'Ging nicht', detail: e.message, life: 6000 })),
  })
}

const menu = ref()
const menuItems = [
  { label: 'Jetzt abgleichen', icon: 'pi pi-sync', command: sync },
  { label: 'Bearbeiten', icon: 'pi pi-pencil', command: () => emit('edit') },
  { separator: true },
  { label: 'Entfernen', icon: 'pi pi-trash', command: remove },
]
</script>

<template>
  <Card class="overflow-hidden" :class="{ 'opacity-60': !printer.enabled }">
    <template #header>
      <div class="flex items-center gap-3 px-5 pt-4">
        <div class="min-w-0 flex-1">
          <div class="truncate text-lg font-semibold">{{ printer.name }}</div>
          <div class="text-xs text-muted-color">{{ typeLabel[printer.type] }} · {{ printer.baseUrl }}</div>
        </div>
        <Tag v-if="!printer.enabled" value="Aus" severity="secondary" />
        <Tag v-if="state" :value="state.label" :severity="state.severity" />
        <Button icon="pi pi-ellipsis-v" text rounded aria-label="Aktionen" :loading="syncing" @click="menu.toggle($event)" />
        <Menu ref="menu" :model="menuItems" popup />
      </div>
    </template>
    <template #content>
      <div class="flex flex-col gap-4">
        <!-- Laufender Druck -->
        <div v-if="status && (status.isActive || status.state === 'Finished' || status.state === 'Cancelled')" class="flex gap-4">
          <PrinterThumbnail v-if="status.hasThumbnail" :printer-id="printer.id" :job-key="status.jobKey" />
          <div class="flex min-w-0 flex-1 flex-col gap-2">
            <div class="truncate font-medium" :title="status.jobName ?? ''">{{ status.jobName ?? 'Druck' }}</div>
            <ProgressBar :value="Math.round(status.progress ?? 0)" class="!h-4" />
            <div class="flex flex-wrap gap-x-4 text-sm text-muted-color">
              <span v-if="status.remainingSeconds != null && status.isActive">noch {{ duration(status.remainingSeconds) }}</span>
              <span v-if="status.elapsedSeconds != null">läuft seit {{ duration(status.elapsedSeconds) }}</span>
              <span v-if="status.layer != null">Schicht {{ status.layer }}<template v-if="status.totalLayers">/{{ status.totalLayers }}</template></span>
            </div>
          </div>
        </div>

        <Message v-if="status?.message" :severity="status.state === 'Offline' ? 'secondary' : 'error'" size="small" :closable="false">
          {{ status.message }}
        </Message>

        <!-- Temperaturen -->
        <div v-if="heaters.length" class="flex flex-wrap gap-2">
          <span v-for="h in heaters" :key="h.name" class="rounded-md bg-surface-100 px-2 py-1 text-xs dark:bg-surface-800">
            <i class="pi pi-sun mr-1 text-orange-400" />{{ h.name }}
            {{ Math.round(h.actual!) }}<template v-if="h.target">/{{ Math.round(h.target) }}</template> °C
          </span>
        </div>

        <!-- Köpfe bzw. Slots -->
        <ul class="divide-y divide-surface-200 rounded-lg border border-surface-200 dark:divide-surface-800 dark:border-surface-800">
          <li
            v-for="t in tools"
            :key="t.tool"
            class="flex cursor-pointer items-center gap-3 px-3 py-2 hover:bg-surface-50 dark:hover:bg-surface-800/50"
            :class="{ 'bg-primary-50 dark:bg-primary-900/20': status?.isActive && status.activeTool === t.tool }"
            @click="emit('tool', t)"
          >
            <span v-if="printer.tools > 1" class="w-14 text-xs text-muted-color">{{ t.place || `#${t.tool + 1}` }}</span>
            <ColorDot :hex="t.colorHex" :size="20" />
            <div class="min-w-0 flex-1">
              <div v-if="t.spoolId" class="truncate text-sm">
                {{ t.itemName }} <span class="font-mono">#{{ t.spoolNumber }}</span>
              </div>
              <div v-else-if="t.itemName" class="truncate text-sm text-orange-500">{{ t.itemName }} – welche Spule?</div>
              <div v-else class="text-sm text-muted-color">frei</div>
              <div v-if="t.reported && (t.reported.type || t.reported.colorHex)" class="flex items-center gap-1 text-xs text-muted-color">
                Drucker: {{ t.reported.type }}
                <ColorDot v-if="t.reported.colorHex" :hex="t.reported.colorHex" :size="10" />
                <template v-if="t.reported.remainPercent != null"> · {{ Math.round(t.reported.remainPercent) }} %</template>
              </div>
            </div>
            <div class="text-right text-sm">
              <div v-if="t.spoolId">{{ grams(t.remainingGrams) }}</div>
              <div v-if="t.neededGrams != null" class="text-xs" :class="t.enough === false ? 'font-semibold text-red-500' : 'text-muted-color'">
                braucht {{ grams(t.neededGrams) }}
              </div>
            </div>
            <i v-if="dryerFor(t.tool)?.humidityPercent != null" v-tooltip="dryerFor(t.tool)!.name" class="pi pi-cloud text-xs text-muted-color" />
          </li>
        </ul>

        <div v-if="status?.dryers?.length" class="flex flex-wrap gap-2 text-xs">
          <span v-for="d in status.dryers" :key="d.name" class="rounded-md bg-surface-100 px-2 py-1 dark:bg-surface-800">
            <i class="pi pi-cloud mr-1" />{{ d.name }}:
            <template v-if="d.humidityPercent != null">{{ Math.round(d.humidityPercent) }} %</template>
            <template v-else-if="d.humidityLevel != null">Stufe {{ d.humidityLevel }}/5</template>
            <template v-if="d.temperature != null"> · {{ Math.round(d.temperature) }} °C</template>
          </span>
        </div>

        <!-- Hinweise -->
        <div v-if="live?.hints.length" class="flex flex-col gap-2">
          <Message v-for="(hint, i) in live.hints" :key="i" :severity="hint.warning ? 'warn' : 'info'" size="small" :closable="false">
            {{ hint.text }}
          </Message>
        </div>
      </div>
    </template>
    <template #footer>
      <div class="flex flex-wrap gap-x-4 text-xs text-muted-color">
        <span>Abgleich {{ ago(printer.lastSyncAt) }}</span>
        <span v-if="printer.readerSeenAt">NFC-Leser {{ ago(printer.readerSeenAt) }}</span>
        <span v-if="printer.lastSyncError" class="text-red-500">{{ printer.lastSyncError }}</span>
      </div>
    </template>
  </Card>
</template>
