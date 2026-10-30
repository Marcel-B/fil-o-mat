<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
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
  return printer.value ? placeName(printer.value.type, printer.value.tools, tool) || 'Eingelegt' : ''
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
    header: 'Container löschen',
    message: `„${c.name}“ löschen? Was darin liegt, wandert nach „Lose“.`,
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: 'Löschen', severity: 'danger' },
    rejectProps: { label: 'Abbrechen', severity: 'secondary', outlined: true },
    accept: async () => {
      try {
        await store.deleteContainer(c.id)
        router.replace({ name: 'containers' })
      } catch (e) {
        toast.add({ severity: 'error', summary: 'Löschen ging nicht', detail: (e as Error).message, life: 6000 })
      }
    },
  })
}
</script>

<template>
  <div v-if="container" class="mx-auto flex max-w-4xl flex-col gap-6">
    <div class="flex flex-wrap items-center gap-3">
      <Button icon="pi pi-arrow-left" text rounded aria-label="Zurück" @click="router.push({ name: 'containers' })" />
      <div class="min-w-0 flex-1">
        <h1 class="text-2xl font-semibold">{{ container.name }}</h1>
        <div v-if="container.description" class="text-muted-color">{{ container.description }}</div>
      </div>
      <template v-if="!printer">
        <Button label="Bearbeiten" icon="pi pi-pencil" text @click="editing = true" />
        <Button v-if="!container.isStandard" label="Löschen" icon="pi pi-trash" severity="danger" text @click="remove" />
      </template>
      <RouterLink v-else to="/drucker"><Button label="Zum Drucker" icon="pi pi-print" text /></RouterLink>
    </div>

    <Card>
      <template #title>{{ printer ? 'Im Drucker' : `Spulen (${spools.length})` }}</template>
      <template #content>
        <ul class="divide-y divide-surface-200 dark:divide-surface-800">
          <template v-if="printer">
            <li v-for="t in tools" :key="t.tool" class="flex items-center gap-3 py-3" :class="{ 'cursor-pointer': t.spool }" @click="t.spool && edit(t.spool)">
              <span class="w-20 text-sm text-muted-color">{{ t.name }}</span>
              <template v-if="t.spool">
                <ColorDot :hex="store.item(t.spool.itemId)?.itemData?.color_hex" :size="20" />
                <span class="flex-1">{{ displayTitle(store.item(t.spool.itemId)) }} <span class="font-mono">#{{ t.spool.number }}</span></span>
                <span>{{ grams(t.spool.remainingWeight ?? store.item(t.spool.itemId)?.netWeight) }}</span>
              </template>
              <span v-else class="flex-1 text-muted-color">frei</span>
            </li>
          </template>
          <li v-for="spool in spools" :key="spool.id" class="flex cursor-pointer items-center gap-3 py-3" @click="edit(spool)">
            <ColorDot :hex="store.item(spool.itemId)?.itemData?.color_hex" :size="20" />
            <span class="flex-1">
              {{ displayTitle(store.item(spool.itemId)) }} <span class="font-mono">#{{ spool.number }}</span>
              <span class="ml-1 text-sm text-muted-color">{{ store.item(spool.itemId)?.itemData?.color }}</span>
            </span>
            <span v-if="spool.remainingWeight != null">{{ grams(spool.remainingWeight) }}</span>
            <span v-else class="text-muted-color">Rest unbekannt</span>
          </li>
          <li v-if="!printer && !spools.length" class="py-3 text-muted-color">Keine Spulen in diesem Container</li>
        </ul>
      </template>
    </Card>

    <Card>
      <template #title>Informationen</template>
      <template #content>
        <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
          <dt class="text-muted-color">RFID-Tag</dt>
          <dd class="font-mono break-all">{{ container.rfidTag }}</dd>
          <template v-if="container.capacity != null">
            <dt class="text-muted-color">Menge</dt>
            <dd>{{ container.capacity }}</dd>
          </template>
        </dl>
      </template>
    </Card>

    <ContainerDialog v-model:visible="editing" :container="container" />
    <SpoolDialog v-model:visible="spoolDialog" :spool="edited" show-item />
  </div>
  <div v-else-if="store.loaded" class="py-16 text-center text-muted-color">
    Container nicht gefunden. <RouterLink :to="{ name: 'containers' }" class="underline">Zur Liste</RouterLink>
  </div>
</template>
