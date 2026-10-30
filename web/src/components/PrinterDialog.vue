<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useDataStore } from '@/stores/data'
import type { Printer, PrinterType } from '@/types'

const props = defineProps<{ printer: Printer | null }>()
const visible = defineModel<boolean>('visible', { required: true })
const store = useDataStore()
const toast = useToast()

const types: { value: PrinterType; label: string; address: string; key: string | null }[] = [
  { value: 'SnapmakerU1', label: 'Snapmaker U1 (paxx12)', address: 'http://<drucker>', key: null },
  { value: 'Moonraker', label: 'Klipper / Moonraker', address: 'http://<drucker>:7125', key: 'API-Schlüssel (nur mit Moonraker-Anmeldung)' },
  { value: 'PrusaLink', label: 'Prusa (PrusaLink)', address: 'http://<drucker>', key: 'API-Schlüssel (am Drucker: Einstellungen > Netzwerk > PrusaLink)' },
  { value: 'Bambu', label: 'Bambu Lab (LAN)', address: 'http://<drucker>', key: 'Zugangscode (am Drucker: Einstellungen > WLAN)' },
  { value: 'Anycubic', label: 'Anycubic (LAN-Modus)', address: 'http://<drucker>', key: null },
]

const name = ref('')
const type = ref<PrinterType>('SnapmakerU1')
const baseUrl = ref('')
const apiKey = ref('')
const removeKey = ref(false)
const enabled = ref(true)
const saving = ref(false)
const info = computed(() => types.find((t) => t.value === type.value)!)

watch(visible, (open) => {
  if (!open) return
  name.value = props.printer?.name ?? ''
  type.value = props.printer?.type ?? 'SnapmakerU1'
  baseUrl.value = props.printer?.baseUrl ?? ''
  apiKey.value = ''
  removeKey.value = false
  enabled.value = props.printer?.enabled ?? true
})

async function save() {
  saving.value = true
  try {
    // Beim Bearbeiten: kein Text lässt den gespeicherten Schlüssel stehen, "" löscht ihn
    const key = removeKey.value ? '' : apiKey.value.trim() || null
    await store.savePrinter(props.printer?.id ?? null, {
      name: name.value,
      type: type.value,
      baseUrl: baseUrl.value,
      apiKey: key,
      enabled: enabled.value,
    })
    visible.value = false
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Speichern ging nicht', detail: (e as Error).message, life: 6000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal :header="printer ? 'Drucker bearbeiten' : 'Neuer Drucker'" class="w-[30rem] max-w-[95vw]">
    <div class="flex flex-col gap-4">
      <label class="flex flex-col gap-1 text-sm">Name<InputText v-model="name" placeholder="z. B. Snapmaker U1" /></label>
      <label class="flex flex-col gap-1 text-sm">
        Typ
        <Select v-model="type" :options="types" option-label="label" option-value="value" :disabled="!!printer" fluid />
      </label>
      <label class="flex flex-col gap-1 text-sm">Adresse<InputText v-model="baseUrl" :placeholder="info.address" class="font-mono" /></label>
      <label v-if="info.key || printer?.hasApiKey" class="flex flex-col gap-1 text-sm">
        {{ info.key ?? 'API-Schlüssel' }}
        <Password v-model="apiKey" :feedback="false" toggle-mask fluid :placeholder="printer?.hasApiKey ? 'gespeichert, leer lassen zum Behalten' : ''" />
      </label>
      <label v-if="printer?.hasApiKey" class="flex items-center gap-2 text-sm">
        <Checkbox v-model="removeKey" binary /> Schlüssel entfernen
      </label>
      <label v-if="printer" class="flex items-center gap-2 text-sm">
        <ToggleSwitch v-model="enabled" /> Automatisch abgleichen
      </label>
    </div>
    <template #footer>
      <Button label="Abbrechen" severity="secondary" text @click="visible = false" />
      <Button label="Speichern" icon="pi pi-check" :loading="saving" :disabled="!name.trim() || !baseUrl.trim()" @click="save" />
    </template>
  </Dialog>
</template>
