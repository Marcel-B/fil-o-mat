<script setup lang="ts">
import { computed, ref } from 'vue'
import { useDataStore } from '@/stores/data'
import { displayTitle, formatDate, grams } from '@/format'
import ColorDot from '@/components/ColorDot.vue'
import SpoolDialog from '@/components/SpoolDialog.vue'
import type { Spool } from '@/types'

const store = useDataStore()
const query = ref('')
const where = ref<'all' | 'printer' | 'storage'>('all')
const whereOptions = [
  { label: 'Alle', value: 'all' },
  { label: 'Im Drucker', value: 'printer' },
  { label: 'Im Lager', value: 'storage' },
]

const rows = computed(() => {
  const q = query.value.trim().toLowerCase()
  return store.spools
    .filter((s) => (where.value === 'printer' ? !!s.printerId : where.value === 'storage' ? !s.printerId : true))
    .map((spool) => {
      const item = store.item(spool.itemId)
      const remaining = spool.remainingWeight ?? item?.netWeight ?? null
      return {
        spool,
        item,
        title: displayTitle(item),
        place: store.placeLabel(store.placeOf(spool)),
        remaining,
        percent: remaining != null && item?.netWeight ? Math.min(100, Math.round((remaining / item.netWeight) * 100)) : null,
      }
    })
    .filter(
      (r) =>
        !q ||
        [r.title, r.place, r.item?.itemData?.color, `#${r.spool.number}`].some((v) => v?.toLowerCase().includes(q)),
    )
})

const edited = ref<Spool | null>(null)
const dialog = ref(false)
function edit(spool: Spool) {
  edited.value = spool
  dialog.value = true
}
</script>

<template>
  <div class="mx-auto flex max-w-6xl flex-col gap-4">
    <div class="flex flex-wrap items-center gap-3">
      <h1 class="text-2xl font-semibold">Spulen</h1>
      <span class="text-muted-color">{{ store.spools.length }} angebrochen</span>
    </div>

    <div class="flex flex-wrap gap-3">
      <IconField class="min-w-60 flex-1">
        <InputIcon class="pi pi-search" />
        <InputText v-model="query" placeholder="Suchen: Sorte, Platz, #Nummer …" fluid />
      </IconField>
      <SelectButton v-model="where" :options="whereOptions" option-label="label" option-value="value" :allow-empty="false" />
    </div>

    <DataTable
      :value="rows"
      data-key="spool.id"
      sort-mode="single"
      row-hover
      :loading="!store.loaded && store.loading"
      class="cursor-pointer overflow-hidden rounded-xl border border-surface-200 dark:border-surface-800"
      @row-click="edit($event.data.spool)"
    >
      <template #empty><div class="py-8 text-center text-muted-color">Keine Spulen</div></template>
      <Column header="Spule" field="title" sortable>
        <template #body="{ data }">
          <div class="flex items-center gap-3">
            <ColorDot :hex="data.item?.itemData?.color_hex" :size="24" />
            <div>
              <div class="font-medium">{{ data.title }} <span class="font-mono">#{{ data.spool.number }}</span></div>
              <div class="text-sm text-muted-color">{{ data.item?.itemData?.color }}</div>
            </div>
          </div>
        </template>
      </Column>
      <Column header="Platz" field="place" sortable />
      <Column header="Rest" field="remaining" sortable class="w-44">
        <template #body="{ data }">
          <div class="flex items-center gap-2">
            <span class="w-16 text-right" :class="{ 'text-muted-color': data.spool.remainingWeight == null }">
              {{ grams(data.remaining) }}
            </span>
            <ProgressBar
              v-if="data.percent != null"
              :value="data.percent"
              :show-value="false"
              class="!h-2 flex-1"
              :pt="{ value: { class: data.percent < 10 ? '!bg-red-500' : data.percent < 25 ? '!bg-orange-400' : '' } }"
            />
          </div>
        </template>
      </Column>
      <Column header="Gewogen" class="hidden md:table-cell">
        <template #body="{ data }">
          <span class="text-sm text-muted-color">{{ data.spool.weighedAt ? formatDate(data.spool.weighedAt) : '–' }}</span>
        </template>
      </Column>
      <Column header="Angebrochen" class="hidden lg:table-cell">
        <template #body="{ data }">
          <span class="text-sm text-muted-color">{{ formatDate(data.spool.openedAt) }}</span>
        </template>
      </Column>
    </DataTable>

    <SpoolDialog v-model:visible="dialog" :spool="edited" show-item />
  </div>
</template>
