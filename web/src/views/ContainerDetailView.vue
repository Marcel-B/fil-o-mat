<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useDataStore } from '@/stores/data'
import { displayTitle, grams, placeName } from '@/format'
import ColorDot from '@/components/ColorDot.vue'
import ContainerDialog from '@/components/ContainerDialog.vue'
import SpoolDialog from '@/components/SpoolDialog.vue'
import type { Spool } from '@/types'

const props = defineProps<{ id: string }>()
const store = useDataStore()
const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const { t } = useI18n()

const container = computed(() => store.container(props.id))
const printer = computed(() => store.printerOwning(props.id))
// Sorten stehen unter "Filamente"; ein Container kennt nur die Spulen, die darin liegen
const spools = computed(() => (printer.value ? [] : store.spoolsInContainer(props.id)))
const tools = computed(() =>
  printer.value
    ? Array.from({ length: printer.value.tools }, (_, tool) => ({
        tool,
        name: toolName(tool),
        spool: store.spoolInPrinter(printer.value!.id, tool),
      }))
    : [],
)
function toolName(tool: number) {
  return printer.value ? placeName(printer.value.type, printer.value.tools, tool) || t('containers.inserted') : ''
}

const editing = ref(false)
const edited = ref<Spool | null>(null)
const spoolDialog = ref(false)
function edit(spool: Spool) {
  edited.value = spool
  spoolDialog.value = true
}

function remove() {
  const c = container.value
  if (!c) return
  confirm.require({
    header: t('containers.deleteTitle'),
    message: t('containers.deleteMessage', { name: c.name }),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: t('common.delete'), severity: 'danger' },
    rejectProps: { label: t('common.cancel'), severity: 'secondary', outlined: true },
    accept: async () => {
      try {
        await store.deleteContainer(c.id)
        router.replace({ name: 'containers' })
      } catch (e) {
        toast.add({ severity: 'error', summary: t('common.deleteFailed'), detail: (e as Error).message, life: 6000 })
      }
    },
  })
}
</script>

<template>
  <div v-if="container" class="mx-auto flex max-w-4xl flex-col gap-6">
    <div class="flex flex-wrap items-center gap-3">
      <Button icon="pi pi-arrow-left" text rounded :aria-label="t('common.back')" @click="router.push({ name: 'containers' })" />
      <div class="min-w-0 flex-1">
        <h1 class="text-2xl font-semibold">{{ container.name }}</h1>
        <div v-if="container.description" class="text-muted-color">{{ container.description }}</div>
      </div>
      <template v-if="!printer">
        <Button :label="t('common.edit')" icon="pi pi-pencil" text @click="editing = true" />
        <Button v-if="!container.isStandard" :label="t('common.delete')" icon="pi pi-trash" severity="danger" text @click="remove" />
      </template>
      <RouterLink v-else to="/drucker"><Button :label="t('containers.toPrinter')" icon="pi pi-print" text /></RouterLink>
    </div>

    <Card>
      <template #title>{{ printer ? t('containers.inPrinter') : t('containers.spools', { n: spools.length }) }}</template>
      <template #content>
        <ul class="divide-y divide-surface-200 dark:divide-surface-800">
          <template v-if="printer">
            <li v-for="tool in tools" :key="tool.tool" class="flex items-center gap-3 py-3" :class="{ 'cursor-pointer': tool.spool }" @click="tool.spool && edit(tool.spool)">
              <span class="w-20 text-sm text-muted-color">{{ tool.name }}</span>
              <template v-if="tool.spool">
                <ColorDot :hex="store.item(tool.spool.itemId)?.itemData?.color_hex" :size="20" />
                <span class="flex-1">{{ displayTitle(store.item(tool.spool.itemId)) }} <span class="font-mono">#{{ tool.spool.number }}</span></span>
                <span>{{ grams(tool.spool.remainingWeight ?? store.item(tool.spool.itemId)?.netWeight) }}</span>
              </template>
              <span v-else class="flex-1 text-muted-color">{{ t('common.free') }}</span>
            </li>
          </template>
          <li v-for="spool in spools" :key="spool.id" class="flex cursor-pointer items-center gap-3 py-3" @click="edit(spool)">
            <ColorDot :hex="store.item(spool.itemId)?.itemData?.color_hex" :size="20" />
            <span class="flex-1">
              {{ displayTitle(store.item(spool.itemId)) }} <span class="font-mono">#{{ spool.number }}</span>
              <span class="ml-1 text-sm text-muted-color">{{ store.item(spool.itemId)?.itemData?.color }}</span>
            </span>
            <span v-if="spool.remainingWeight != null">{{ grams(spool.remainingWeight) }}</span>
            <span v-else class="text-muted-color">{{ t('common.remainingUnknown') }}</span>
          </li>
          <li v-if="!printer && !spools.length" class="py-3 text-muted-color">{{ t('containers.empty') }}</li>
        </ul>
      </template>
    </Card>

    <Card>
      <template #title>{{ t('containers.info') }}</template>
      <template #content>
        <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
          <dt class="text-muted-color">{{ t('containers.rfidTag') }}</dt>
          <dd class="font-mono break-all">{{ container.rfidTag }}</dd>
          <template v-if="container.capacity != null">
            <dt class="text-muted-color">{{ t('common.quantity') }}</dt>
            <dd>{{ container.capacity }}</dd>
          </template>
        </dl>
      </template>
    </Card>

    <ContainerDialog v-model:visible="editing" :container="container" />
    <SpoolDialog v-model:visible="spoolDialog" :spool="edited" show-item />
  </div>
  <div v-else-if="store.loaded" class="py-16 text-center text-muted-color">
    {{ t('containers.notFound') }} <RouterLink :to="{ name: 'containers' }" class="underline">{{ t('common.toList') }}</RouterLink>
  </div>
</template>
