<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useDataStore } from '@/stores/data'
import { displayTitle, grams, printerPlace } from '@/format'
import ColorDot from './ColorDot.vue'
import type { Printer } from '@/types'

const props = defineProps<{ printer: Printer | null; tool: number }>()
const visible = defineModel<boolean>('visible', { required: true })
const store = useDataStore()
const toast = useToast()

const choice = ref<string | null>(null)
const openNew = ref<string | null>(null)
const saving = ref(false)
watch(visible, (open) => {
  if (open) {
    choice.value = null
    openNew.value = null
  }
})

// Lager-Spulen zuerst, dann die aus anderen Drucker-Plätzen
const options = computed(() =>
  store.spools
    .map((s) => {
      const item = store.item(s.itemId)
      return {
        id: s.id,
        label: `${displayTitle(item)} #${s.number}`,
        detail: [item?.itemData?.color, store.placeLabel(store.placeOf(s)), grams(s.remainingWeight ?? item?.netWeight)].filter(Boolean).join(' · '),
        color: item?.itemData?.color_hex,
        inPrinter: !!s.printerId,
      }
    })
    .sort((a, b) => Number(a.inPrinter) - Number(b.inPrinter) || a.label.localeCompare(b.label, 'de')),
)
const itemOptions = computed(() =>
  store.items
    .map((i) => ({ id: i.id, label: `${displayTitle(i)}${i.itemData?.color ? ` · ${i.itemData.color}` : ''}`, color: i.itemData?.color_hex, stock: i.spoolsInStock ?? 0 }))
    .sort((a, b) => a.label.localeCompare(b.label, 'de')),
)

async function save() {
  if (!props.printer) return
  const place = { printerId: props.printer.id, tool: props.tool, containerId: null }
  saving.value = true
  try {
    if (choice.value) {
      const spool = store.spools.find((s) => s.id === choice.value)!
      await store.updateSpool(spool.id, spool.remainingWeight, place)
    } else if (openNew.value) {
      const spool = await store.openSpool(openNew.value, place)
      toast.add({ severity: 'success', summary: `Spule #${spool.number} angebrochen`, detail: 'Nummer mit Stift auf die Spule schreiben.', life: 6000 })
    }
    visible.value = false
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Ging nicht', detail: (e as Error).message, life: 6000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal :header="printer ? `${printerPlace(printer, tool)}: Spule einlegen` : ''" class="w-[32rem] max-w-[95vw]">
    <div class="flex flex-col gap-4">
      <label class="flex flex-col gap-1 text-sm">
        Angebrochene Spule
        <Select v-model="choice" :options="options" option-label="label" option-value="id" filter show-clear placeholder="Spule wählen" fluid @change="openNew = null">
          <template #option="{ option }">
            <div class="flex items-center gap-2">
              <ColorDot :hex="option.color" :size="16" />
              <div>
                <div>{{ option.label }}</div>
                <div class="text-xs text-muted-color">{{ option.detail }}</div>
              </div>
            </div>
          </template>
        </Select>
      </label>
      <div class="text-center text-sm text-muted-color">oder</div>
      <label class="flex flex-col gap-1 text-sm">
        Neue Spule einer Sorte anbrechen
        <Select v-model="openNew" :options="itemOptions" option-label="label" option-value="id" filter show-clear placeholder="Sorte wählen" fluid @change="choice = null">
          <template #option="{ option }">
            <div class="flex items-center gap-2">
              <ColorDot :hex="option.color" :size="16" />
              <span class="flex-1">{{ option.label }}</span>
              <span v-if="option.stock" class="text-xs text-muted-color">Vorrat {{ option.stock }}</span>
            </div>
          </template>
        </Select>
      </label>
    </div>
    <template #footer>
      <Button label="Abbrechen" severity="secondary" text @click="visible = false" />
      <Button label="Einlegen" icon="pi pi-check" :loading="saving" :disabled="!choice && !openNew" @click="save" />
    </template>
  </Dialog>
</template>
