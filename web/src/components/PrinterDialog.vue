<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useDataStore } from '@/stores/data'
import type { Printer, PrinterType } from '@/types'

const props = defineProps<{ printer: Printer | null }>()
const visible = defineModel<boolean>('visible', { required: true })
const store = useDataStore()
const toast = useToast()
const { t } = useI18n()

const types = computed<{ value: PrinterType; label: string; address: string; key: string | null }[]>(() => {
  const host = `http://<${t('printers.printerHost')}>`
  return [
    { value: 'SnapmakerU1', label: t('printers.typeSnapmaker'), address: host, key: null },
    { value: 'Moonraker', label: 'Klipper / Moonraker', address: `${host}:7125`, key: t('printers.keyMoonraker') },
    { value: 'PrusaLink', label: t('printers.typePrusa'), address: host, key: t('printers.keyPrusa') },
    { value: 'Bambu', label: 'Bambu Lab (LAN)', address: host, key: t('printers.keyBambu') },
    { value: 'Anycubic', label: t('printers.typeAnycubic'), address: host, key: null },
  ]
})

const name = ref('')
const type = ref<PrinterType>('SnapmakerU1')
const baseUrl = ref('')
const apiKey = ref('')
const removeKey = ref(false)
const enabled = ref(true)
const saving = ref(false)
const info = computed(() => types.value.find((option) => option.value === type.value)!)

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
    toast.add({ severity: 'error', summary: t('common.saveFailed'), detail: (e as Error).message, life: 6000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal :header="printer ? t('printers.editTitle') : t('printers.new')" class="w-[30rem] max-w-[95vw]">
    <div class="flex flex-col gap-4">
      <label class="flex flex-col gap-1 text-sm">{{ t('common.name') }}<InputText v-model="name" :placeholder="t('printers.namePlaceholder')" /></label>
      <label class="flex flex-col gap-1 text-sm">
        {{ t('printers.type') }}
        <Select v-model="type" :options="types" option-label="label" option-value="value" :disabled="!!printer" fluid />
      </label>
      <label class="flex flex-col gap-1 text-sm">{{ t('printers.address') }}<InputText v-model="baseUrl" :placeholder="info.address" class="font-mono" /></label>
      <label v-if="info.key || printer?.hasApiKey" class="flex flex-col gap-1 text-sm">
        {{ info.key ?? t('printers.apiKey') }}
        <Password v-model="apiKey" :feedback="false" toggle-mask fluid :placeholder="printer?.hasApiKey ? t('common.keptPlaceholder') : ''" />
      </label>
      <label v-if="printer?.hasApiKey" class="flex items-center gap-2 text-sm">
        <Checkbox v-model="removeKey" binary /> {{ t('printers.removeKey') }}
      </label>
      <label v-if="printer" class="flex items-center gap-2 text-sm">
        <ToggleSwitch v-model="enabled" /> {{ t('printers.autoSync') }}
      </label>
    </div>
    <template #footer>
      <Button :label="t('common.cancel')" severity="secondary" text @click="visible = false" />
      <Button :label="t('common.save')" icon="pi pi-check" :loading="saving" :disabled="!name.trim() || !baseUrl.trim()" @click="save" />
    </template>
  </Dialog>
</template>
