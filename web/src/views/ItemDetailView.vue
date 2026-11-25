<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { api } from '@/api'
import { useDataStore } from '@/stores/data'
import { displayTitle, formatDateTime, grams } from '@/format'
import { emptyForm, formFromItem, inputFromForm } from '@/itemForm'
import ColorDot from '@/components/ColorDot.vue'
import ItemForm from '@/components/ItemForm.vue'
import PlaceSelect from '@/components/PlaceSelect.vue'
import SpoolDialog from '@/components/SpoolDialog.vue'
import type { Consumption, Item, Spool, SpoolPlace } from '@/types'

const props = defineProps<{ id: string }>()
const store = useDataStore()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()
const { t } = useI18n()

const item = computed(() => store.item(props.id))
const spools = computed(() => store.spoolsOf(props.id))
const duplicates = computed(() => (item.value ? store.possibleDuplicates(item.value) : []))
const tags = computed(() => (item.value ? [item.value.rfidTag, ...item.value.extraTags].filter(Boolean) : []))

const form = ref(emptyForm())
// Stand aus dem Backend; geändert ist das Formular, wenn es davon abweicht
const baseline = ref('')
const dirty = computed(() => JSON.stringify(form.value) !== baseline.value)
const saving = ref(false)

let loadedId: string | null = null
function load(it: Item) {
  loadedId = it.id
  form.value = formFromItem(it)
  baseline.value = JSON.stringify(form.value)
}

// Das Neuladen alle zwei Minuten überschreibt nichts, was gerade jemand eintippt
watch(
  item,
  (it) => {
    if (it && (it.id !== loadedId || !dirty.value)) load(it)
  },
  { immediate: true },
)

async function save() {
  const it = item.value
  if (!it) return
  saving.value = true
  try {
    load(await store.updateItem(it.id, { name: it.name, description: it.description, ...inputFromForm(form.value, it) }))
    toast.add({ severity: 'success', summary: t('common.saved'), life: 2500 })
  } catch (e) {
    toast.add({ severity: 'error', summary: t('common.saveFailed'), detail: (e as Error).message, life: 6000 })
  } finally {
    saving.value = false
  }
}

function reset() {
  if (item.value) load(item.value)
}

function remove() {
  const it = item.value
  if (!it) return
  confirm.require({
    header: t('item.deleteTitle'),
    message: t('item.deleteMessage', { name: displayTitle(it) }),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: t('common.delete'), severity: 'danger' },
    rejectProps: { label: t('common.cancel'), severity: 'secondary', outlined: true },
    accept: async () => {
      try {
        await store.deleteItem(it.id)
        router.replace({ name: 'items' })
      } catch (e) {
        toast.add({ severity: 'error', summary: t('common.deleteFailed'), detail: (e as Error).message, life: 6000 })
      }
    },
  })
}

function merge(other: Item) {
  const it = item.value
  if (!it) return
  confirm.require({
    header: t('item.mergeTitle'),
    message: t('item.mergeMessage', { name: displayTitle(other) }),
    acceptProps: { label: t('item.take') },
    rejectProps: { label: t('common.cancel'), severity: 'secondary', outlined: true },
    accept: async () => {
      try {
        const result = await store.mergeItem(it.id, other.id)
        load(result.item)
        const renumbered = result.renumbered.map((r) => `#${r.oldNumber} → #${r.newNumber}`).join(', ')
        toast.add({
          severity: 'success',
          summary: t('item.merged'),
          detail: renumbered ? t('item.renumbered', { list: renumbered }) : undefined,
          life: renumbered ? 15000 : 3000,
        })
      } catch (e) {
        toast.add({ severity: 'error', summary: t('common.failed'), detail: (e as Error).message, life: 6000 })
      }
    },
  })
}

// Spulen
const editedSpool = ref<Spool | null>(null)
const spoolDialog = ref(false)
function editSpool(spool: Spool) {
  editedSpool.value = spool
  spoolDialog.value = true
}

const opening = ref(false)
const openDialog = ref(false)
const openPlace = ref<SpoolPlace>({ printerId: null, tool: null, containerId: null })
const openWeight = ref<number | null>(null)
function startOpen() {
  openPlace.value = { printerId: null, tool: null, containerId: item.value?.containerId ?? null }
  openWeight.value = null
  openDialog.value = true
}
async function openSpool() {
  opening.value = true
  try {
    const spool = await store.openSpool(props.id, openPlace.value, openWeight.value)
    openDialog.value = false
    toast.add({ severity: 'success', summary: t('common.spoolOpened', { number: spool.number }), detail: t('common.writeNumber'), life: 6000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: t('common.failed'), detail: (e as Error).message, life: 6000 })
  } finally {
    opening.value = false
  }
}

// Verbrauch
const consumption = ref<Consumption[]>([])
async function loadConsumption() {
  try {
    consumption.value = await api.get<Consumption[]>(`/consumption?itemId=${encodeURIComponent(props.id)}`)
  } catch {
    consumption.value = []
  }
}
watch(() => props.id, loadConsumption, { immediate: true })
const consumedTotal = computed(() => consumption.value.reduce((sum, c) => sum + c.grams, 0))
</script>

<template>
  <div v-if="item" class="mx-auto flex max-w-5xl flex-col gap-6">
    <div class="flex flex-wrap items-center gap-3">
      <Button icon="pi pi-arrow-left" text rounded :aria-label="t('common.back')" @click="router.push({ name: 'items' })" />
      <ColorDot :hex="item.itemData?.color_hex" :size="36" />
      <div class="min-w-0 flex-1">
        <h1 class="text-2xl font-semibold">{{ displayTitle(item) }}</h1>
        <div class="text-muted-color">{{ item.itemData?.color }}</div>
      </div>
      <Button :label="t('common.delete')" icon="pi pi-trash" severity="danger" text @click="remove" />
    </div>

    <Message v-if="duplicates.length" severity="warn" :closable="false">
      <div class="flex flex-col gap-2">
        <span>{{ t('item.duplicates') }}</span>
        <div v-for="other in duplicates" :key="other.id" class="flex flex-wrap items-center gap-2">
          <RouterLink :to="{ name: 'item', params: { id: other.id } }" class="font-medium underline">
            {{ displayTitle(other) }}
          </RouterLink>
          <span class="text-sm">{{ t('item.duplicateInfo', { spools: t('common.spools', store.spoolsOf(other.id).length), stock: other.spoolsInStock ?? 0 }) }}</span>
          <Button :label="t('item.takeHere')" size="small" outlined @click="merge(other)" />
        </div>
      </div>
    </Message>

    <Card>
      <template #content>
        <ItemForm v-model="form" />
      </template>
      <template #footer>
        <div class="flex justify-end gap-2">
          <Button :label="t('item.discard')" severity="secondary" text :disabled="!dirty" @click="reset" />
          <Button :label="t('common.save')" icon="pi pi-check" :disabled="!dirty" :loading="saving" @click="save" />
        </div>
      </template>
    </Card>

    <Card>
      <template #title>
        <div class="flex items-center gap-2">
          <span class="flex-1">{{ t('item.openSpools') }}</span>
          <Button :label="t('item.openNew')" icon="pi pi-plus" size="small" @click="startOpen" />
        </div>
      </template>
      <template #subtitle>
        {{ t('item.spoolsHint') }}
      </template>
      <template #content>
        <div v-if="!spools.length" class="text-muted-color">{{ t('item.noSpool') }}</div>
        <ul class="divide-y divide-surface-200 dark:divide-surface-800">
          <li
            v-for="spool in spools"
            :key="spool.id"
            class="flex cursor-pointer items-center gap-3 py-3 hover:bg-surface-50 dark:hover:bg-surface-800/50"
            @click="editSpool(spool)"
          >
            <span class="w-12 font-mono text-lg font-semibold">#{{ spool.number }}</span>
            <span class="flex-1">{{ store.placeLabel(store.placeOf(spool)) }}</span>
            <span v-if="spool.remainingWeight != null">{{ grams(spool.remainingWeight) }}</span>
            <span v-else class="text-muted-color">{{ t('common.remainingUnknown') }}</span>
            <ProgressBar
              v-if="spool.remainingWeight != null && item.netWeight"
              :value="Math.min(100, Math.round((spool.remainingWeight / item.netWeight) * 100))"
              :show-value="false"
              class="!h-2 hidden w-24 sm:block"
            />
            <i class="pi pi-chevron-right text-muted-color" />
          </li>
        </ul>
      </template>
    </Card>

    <Card>
      <template #title>{{ t('item.tags') }}</template>
      <template #subtitle>{{ t('item.tagsHint') }}</template>
      <template #content>
        <div v-if="!tags.length" class="text-muted-color">{{ t('item.noTag') }}</div>
        <div class="flex flex-wrap gap-2">
          <Tag v-for="tag in tags" :key="tag" :value="tag" severity="secondary" class="font-mono" />
        </div>
      </template>
    </Card>

    <Card>
      <template #title>{{ t('item.consumption') }}</template>
      <template #subtitle v-if="consumption.length">{{ t('item.consumptionSummary', { n: consumption.length, grams: grams(consumedTotal) }, consumption.length) }}</template>
      <template #content>
        <DataTable :value="consumption" paginator :rows="10" size="small" data-key="id">
          <template #empty><span class="text-muted-color">{{ t('item.noConsumption') }}</span></template>
          <Column :header="t('printers.end')">
            <template #body="{ data }">{{ formatDateTime(data.endedAt) }}</template>
          </Column>
          <Column :header="t('printers.job')" field="jobName" class="max-w-64 truncate" />
          <Column :header="t('common.printer')">
            <template #body="{ data }">{{ store.printer(data.printerId)?.name ?? '–' }}</template>
          </Column>
          <Column :header="t('common.spool')">
            <template #body="{ data }">
              {{ data.spoolId ? `#${store.spools.find((s) => s.id === data.spoolId)?.number ?? '–'}` : '' }}
            </template>
          </Column>
          <Column :header="t('common.quantity')" class="text-right">
            <template #body="{ data }">
              {{ grams(data.grams) }}
              <Tag v-if="data.status !== 'completed'" :value="t('item.cancelled')" severity="warn" class="ml-1" />
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <SpoolDialog v-model:visible="spoolDialog" :spool="editedSpool" />

    <Dialog v-model:visible="openDialog" modal :header="t('item.openNew')" class="w-[28rem] max-w-[95vw]">
      <div class="flex flex-col gap-4">
        <p v-if="item.spoolsInStock" class="text-sm text-muted-color">{{ t('item.stockDrops', { n: item.spoolsInStock - 1 }) }}</p>
        <label class="flex flex-col gap-1 text-sm">
          {{ t('common.place') }}
          <PlaceSelect v-model="openPlace" />
        </label>
        <label class="flex flex-col gap-1 text-sm">
          {{ t('common.remainingWeight') }}
          <InputNumber
            v-model="openWeight"
            suffix=" g"
            :min="0"
            fluid
            :placeholder="item.netWeight ? t('item.fullSpool', { grams: item.netWeight }) : t('common.unknown')"
          />
        </label>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" severity="secondary" text @click="openDialog = false" />
        <Button :label="t('item.open')" icon="pi pi-check" :loading="opening" @click="openSpool" />
      </template>
    </Dialog>
  </div>
  <div v-else-if="store.loaded" class="mx-auto max-w-5xl py-16 text-center text-muted-color">
    {{ t('item.notFound') }}
    <RouterLink :to="{ name: 'items' }" class="underline">{{ t('common.toList') }}</RouterLink>
  </div>
</template>
