<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { LOOSE_ID, useDataStore } from '@/stores/data'
import { printerPlace } from '@/format'
import type { SpoolPlace } from '@/types'

const place = defineModel<SpoolPlace>({ required: true })
const props = defineProps<{ inputId?: string; spoolId?: string }>()
const store = useDataStore()
const { t } = useI18n()

// Werte als Text, weil Select Objekte nur über Referenz vergleicht
function encode(p: SpoolPlace): string {
  if (p.printerId) return `p:${p.printerId}:${p.tool ?? 0}`
  if (p.containerId) return `c:${p.containerId}`
  return store.container(LOOSE_ID) ? `c:${LOOSE_ID}` : 's:'
}

function decode(value: string): SpoolPlace {
  const [kind, id, tool] = value.split(':')
  if (kind === 'p') return { printerId: id, tool: Number(tool), containerId: null }
  if (kind === 'c') return { printerId: null, tool: null, containerId: id }
  return { printerId: null, tool: null, containerId: null }
}

const groups = computed(() => {
  const storage = store.storageContainers.map((c) => ({ label: c.name, value: `c:${c.id}` }))
  if (!store.container(LOOSE_ID)) storage.unshift({ label: t('common.storage'), value: 's:' })
  const printers = store.printers.flatMap((p) =>
    Array.from({ length: p.tools }, (_, tool) => {
      const occupant = store.spoolInPrinter(p.id, tool)
      const item = store.item(occupant?.itemId)
      return {
        label:
          occupant && occupant.id !== props.spoolId
            ? t('place.occupied', { place: printerPlace(p, tool), name: item?.name ?? t('common.spool'), number: occupant.number })
            : printerPlace(p, tool),
        value: `p:${p.id}:${tool}`,
      }
    }),
  )
  return [
    { label: t('common.storage'), items: storage },
    ...(printers.length ? [{ label: t('common.printer'), items: printers }] : []),
  ]
})

const value = computed({
  get: () => encode(place.value),
  set: (v: string) => (place.value = decode(v)),
})
</script>

<template>
  <Select
    v-model="value"
    :input-id="inputId"
    :options="groups"
    option-label="label"
    option-value="value"
    option-group-label="label"
    option-group-children="items"
    class="w-full"
    filter
  />
</template>
