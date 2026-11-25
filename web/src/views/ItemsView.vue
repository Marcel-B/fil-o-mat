<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useDataStore } from '@/stores/data'
import { decimal, displayTitle, grams } from '@/format'
import { defaultName, emptyForm, inputFromForm } from '@/itemForm'
import ColorDot from '@/components/ColorDot.vue'
import ItemForm from '@/components/ItemForm.vue'
import type { Item } from '@/types'

const store = useDataStore()
const router = useRouter()
const toast = useToast()
const { t } = useI18n()

const query = ref('')
const typeFilter = ref<string | null>(null)

const types = computed(() =>
  [...new Set(store.items.map((i) => i.itemData?.type?.trim() || t('items.noType')))].sort((a, b) => a.localeCompare(b, 'de')),
)

interface Row {
  item: Item
  title: string
  type: string
  spoolText: string
  spoolGrams: number
  stock: number
  duplicate: boolean
}

const rows = computed<Row[]>(() => {
  const q = query.value.trim().toLowerCase()
  return store.items
    .filter((item) => {
      const d = item.itemData
      if (typeFilter.value && (d?.type?.trim() || t('items.noType')) !== typeFilter.value) return false
      if (!q) return true
      return [item.name, d?.type, d?.brand, d?.variant, d?.color, d?.color_hex, ...item.extraTags, item.rfidTag].some((v) =>
        v?.toLowerCase().includes(q),
      )
    })
    .map((item) => {
      const spools = store.spoolsOf(item.id)
      return {
        item,
        title: displayTitle(item),
        type: item.itemData?.type?.trim() || t('items.noType'),
        spoolText: spools
          .map((s) => `#${s.number}` + (s.remainingWeight != null ? ` ${grams(s.remainingWeight)}` : ''))
          .join(', '),
        spoolGrams: spools.reduce((sum, s) => sum + (s.remainingWeight ?? item.netWeight ?? 0), 0),
        stock: item.spoolsInStock ?? 0,
        duplicate: store.possibleDuplicates(item).length > 0,
      }
    })
    .sort((a, b) => a.type.localeCompare(b.type, 'de') || a.title.localeCompare(b.title, 'de'))
})

const creating = ref(false)
const saving = ref(false)
const form = ref(emptyForm())

function openCreate() {
  form.value = emptyForm()
  creating.value = true
}

async function create() {
  if (!form.value.type.trim()) {
    toast.add({ severity: 'warn', summary: t('items.typeMissing'), life: 3000 })
    return
  }
  saving.value = true
  try {
    const item = await store.createItem({ name: defaultName(form.value), description: '', ...inputFromForm(form.value) })
    creating.value = false
    router.push({ name: 'item', params: { id: item.id } })
  } catch (e) {
    toast.add({ severity: 'error', summary: t('items.createFailed'), detail: (e as Error).message, life: 6000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="mx-auto flex max-w-6xl flex-col gap-4">
    <div class="flex flex-wrap items-center gap-3">
      <h1 class="text-2xl font-semibold">{{ t('items.title') }}</h1>
      <span class="text-muted-color">{{ t('items.count', store.items.length) }}</span>
      <span class="flex-1" />
      <Button :label="t('items.new')" icon="pi pi-plus" @click="openCreate" />
    </div>

    <div class="flex flex-wrap gap-3">
      <IconField class="min-w-60 flex-1">
        <InputIcon class="pi pi-search" />
        <InputText v-model="query" :placeholder="t('items.search')" fluid />
      </IconField>
      <Select v-model="typeFilter" :options="types" :placeholder="t('items.allTypes')" show-clear class="w-44" />
    </div>

    <DataTable
      :value="rows"
      data-key="item.id"
      row-group-mode="subheader"
      group-rows-by="type"
      :loading="!store.loaded && store.loading"
      :pt="{ rowGroupHeaderCell: { colspan: 5 } }"
      row-hover
      class="cursor-pointer overflow-hidden rounded-xl border border-surface-200 dark:border-surface-800"
      @row-click="router.push({ name: 'item', params: { id: $event.data.item.id } })"
    >
      <template #groupheader="{ data }">
        <span class="font-semibold">{{ data.type }}</span>
        <span class="ml-2 text-muted-color">({{ rows.filter((r) => r.type === data.type).length }})</span>
      </template>
      <template #empty>
        <div class="py-8 text-center text-muted-color">
          {{ query || typeFilter ? t('items.nothingFound') : t('items.none') }}
        </div>
      </template>
      <Column :header="t('items.kind')">
        <template #body="{ data }">
          <div class="flex items-center gap-3">
            <ColorDot :hex="data.item.itemData?.color_hex" :size="28" />
            <div class="min-w-0">
              <div class="font-medium">{{ data.title }}</div>
              <div class="text-sm text-muted-color">
                {{ data.item.itemData?.color || (data.item.itemData?.color_hex ? `#${data.item.itemData.color_hex}` : '') }}
                <span v-if="data.duplicate" class="ml-1 text-orange-500"><i class="pi pi-exclamation-triangle text-xs" /> {{ t('items.possibleDuplicate') }}</span>
              </div>
            </div>
          </div>
        </template>
      </Column>
      <Column :header="t('items.opened')" class="hidden md:table-cell">
        <template #body="{ data }">
          <span v-if="data.spoolText" class="text-sm">{{ data.spoolText }}</span>
          <span v-else class="text-muted-color">–</span>
        </template>
      </Column>
      <Column :header="t('items.stock')" class="w-24 text-right">
        <template #body="{ data }">
          <Tag v-if="data.stock > 0" :value="t('items.full', { n: data.stock })" severity="secondary" />
        </template>
      </Column>
      <Column :header="t('items.total')" class="hidden w-28 text-right sm:table-cell">
        <template #body="{ data }">
          {{ grams(data.spoolGrams + data.stock * (data.item.netWeight ?? 0)) }}
        </template>
      </Column>
      <Column header="TD" class="hidden w-20 text-right lg:table-cell">
        <template #body="{ data }">
          {{ data.item.transmissionDistance != null ? `${decimal(data.item.transmissionDistance, 2)} mm` : '' }}
        </template>
      </Column>
    </DataTable>

    <Dialog v-model:visible="creating" modal :header="t('items.new')" class="w-[48rem] max-w-[95vw]">
      <ItemForm v-model="form" creating />
      <p class="mt-4 text-xs text-muted-color">
        {{ t('items.tagHint') }}
      </p>
      <template #footer>
        <Button :label="t('common.cancel')" severity="secondary" text @click="creating = false" />
        <Button :label="t('items.create')" icon="pi pi-check" :loading="saving" @click="create" />
      </template>
    </Dialog>
  </div>
</template>
