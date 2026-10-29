<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useDataStore } from '@/stores/data'
import { displayTitle, grams } from '@/format'
import type { Spool, SpoolPlace } from '@/types'
import ColorDot from './ColorDot.vue'
import PlaceSelect from './PlaceSelect.vue'

const props = defineProps<{ spool: Spool | null; showItem?: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })

const store = useDataStore()
const toast = useToast()
const confirm = useConfirm()

const item = computed(() => store.item(props.spool?.itemId))
const remaining = ref<number | null>(null)
const weighed = ref<number | null>(null)
const emptySpool = ref<number | null>(null)
const place = ref<SpoolPlace>({ printerId: null, tool: null, containerId: null })
const saving = ref(false)

watch(
  () => [visible.value, props.spool] as const,
  ([open, spool]) => {
    if (!open || !spool) return
    remaining.value = spool.remainingWeight
    weighed.value = null
    emptySpool.value = item.value?.spoolWeight ?? null
    place.value = store.placeOf(spool)
  },
  { immediate: true },
)

// Gewogen mit Spule minus leere Spule ergibt das Restgewicht
watch([weighed, emptySpool], ([gross, empty]) => {
  if (gross != null && empty != null) remaining.value = Math.max(0, Math.round((gross - empty) * 10) / 10)
})

async function save() {
  if (!props.spool) return
  saving.value = true
  try {
    await store.updateSpool(props.spool.id, remaining.value, place.value)
    toast.add({ severity: 'success', summary: `Spule #${props.spool.number} gespeichert`, life: 2500 })
    visible.value = false
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Speichern ging nicht', detail: (e as Error).message, life: 6000 })
  } finally {
    saving.value = false
  }
}

function markEmpty() {
  const spool = props.spool
  if (!spool) return
  confirm.require({
    header: `Spule #${spool.number} ist leer`,
    message: 'Die Spule verschwindet aus den Listen; ihr Verbrauch bleibt in der Statistik.',
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: 'Ist leer', severity: 'danger' },
    rejectProps: { label: 'Abbrechen', severity: 'secondary', outlined: true },
    accept: async () => {
      try {
        await store.markSpoolEmpty(spool.id)
        visible.value = false
      } catch (e) {
        toast.add({ severity: 'error', summary: 'Ging nicht', detail: (e as Error).message, life: 6000 })
      }
    },
  })
}
</script>

<template>
  <Dialog v-model:visible="visible" modal :header="spool ? `Spule #${spool.number}` : 'Spule'" class="w-[32rem] max-w-[95vw]">
    <div v-if="spool" class="flex flex-col gap-5">
      <RouterLink
        v-if="showItem && item"
        :to="{ name: 'item', params: { id: item.id } }"
        class="flex items-center gap-3 rounded-lg border border-surface-200 p-3 hover:bg-surface-100 dark:border-surface-700 dark:hover:bg-surface-800"
        @click="visible = false"
      >
        <ColorDot :hex="item.itemData?.color_hex" :size="28" />
        <div class="min-w-0 flex-1">
          <div class="font-medium">{{ displayTitle(item) }}</div>
          <div class="text-sm text-muted-color">
            {{ [item.itemData?.color, item.itemData?.min_temp && `${item.itemData.min_temp}–${item.itemData.max_temp} °C`].filter(Boolean).join(' · ') }}
            <template v-if="item.netWeight"> · neu {{ grams(item.netWeight) }}</template>
          </div>
        </div>
        <i class="pi pi-chevron-right text-muted-color" />
      </RouterLink>

      <fieldset class="flex flex-col gap-3">
        <legend class="mb-2 text-sm font-semibold text-muted-color">Wiegen</legend>
        <div class="grid grid-cols-2 gap-3">
          <label class="flex flex-col gap-1 text-sm">
            Gewogen mit Spule
            <InputNumber locale="de-DE" v-model="weighed" suffix=" g" :min="0" :max-fraction-digits="1" fluid />
          </label>
          <label class="flex flex-col gap-1 text-sm">
            Leere Spule
            <InputNumber locale="de-DE" v-model="emptySpool" suffix=" g" :min="0" :max-fraction-digits="1" fluid />
          </label>
        </div>
        <label class="flex flex-col gap-1 text-sm">
          Restgewicht
          <InputNumber locale="de-DE" v-model="remaining" suffix=" g" :min="0" :max-fraction-digits="1" fluid placeholder="unbekannt" />
        </label>
      </fieldset>

      <label class="flex flex-col gap-1 text-sm">
        Platz
        <PlaceSelect v-model="place" :spool-id="spool.id" />
      </label>
      <p class="text-xs text-muted-color">Liegt an dem Platz schon eine Spule, wandert sie ins Lager.</p>
    </div>

    <template #footer>
      <Button label="Spule ist leer" icon="pi pi-trash" severity="danger" text :disabled="saving" @click="markEmpty" />
      <span class="flex-1" />
      <Button label="Abbrechen" severity="secondary" text @click="visible = false" />
      <Button label="Speichern" icon="pi pi-check" :loading="saving" @click="save" />
    </template>
  </Dialog>
</template>
