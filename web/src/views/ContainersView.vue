<script setup lang="ts">
import { computed, ref } from 'vue'
import { PRINTER_STANDARD_ID, useDataStore } from '@/stores/data'
import { grams } from '@/format'
import ColorDot from '@/components/ColorDot.vue'
import ContainerDialog from '@/components/ContainerDialog.vue'

const store = useDataStore()
const creating = ref(false)

const cards = computed(() =>
  store.containers
    // Der alte Standard-Container "Drucker" ist leer, seit jeder Drucker einen eigenen hat
    .filter((c) => c.id !== PRINTER_STANDARD_ID)
    .map((container) => {
      const printer = store.printerOwning(container.id)
      const spools = printer ? store.spools.filter((s) => s.printerId === printer.id) : store.spoolsInContainer(container.id)
      return {
        container,
        printer,
        spools,
        grams: spools.reduce((sum, s) => sum + (s.remainingWeight ?? store.item(s.itemId)?.netWeight ?? 0), 0),
      }
    })
    .sort((a, b) => Number(!!a.printer) - Number(!!b.printer) || Number(b.container.isStandard) - Number(a.container.isStandard) || a.container.name.localeCompare(b.container.name, 'de')),
)
</script>

<template>
  <div class="mx-auto flex max-w-6xl flex-col gap-4">
    <div class="flex flex-wrap items-center gap-3">
      <h1 class="text-2xl font-semibold">Container</h1>
      <span class="flex-1" />
      <Button label="Neuer Container" icon="pi pi-plus" @click="creating = true" />
    </div>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <RouterLink
        v-for="card in cards"
        :key="card.container.id"
        :to="{ name: 'container', params: { id: card.container.id } }"
        class="flex flex-col gap-3 rounded-xl border border-surface-200 bg-surface-0 p-4 transition hover:border-primary-400 dark:border-surface-800 dark:bg-surface-900"
      >
        <div class="flex items-center gap-2">
          <i :class="card.printer ? 'pi pi-print' : 'pi pi-box'" class="text-muted-color" />
          <span class="flex-1 truncate font-semibold">{{ card.container.name }}</span>
          <Tag v-if="card.printer" value="Drucker" severity="secondary" />
          <Tag v-else-if="card.container.isStandard" value="Standard" severity="secondary" />
        </div>
        <p v-if="card.container.description" class="line-clamp-2 text-sm text-muted-color">{{ card.container.description }}</p>
        <div class="flex flex-wrap items-center gap-1">
          <ColorDot v-for="spool in card.spools.slice(0, 16)" :key="spool.id" :hex="store.item(spool.itemId)?.itemData?.color_hex" :size="18" />
          <span v-if="card.spools.length > 16" class="text-sm text-muted-color">+{{ card.spools.length - 16 }}</span>
        </div>
        <div class="mt-auto text-sm text-muted-color">
          {{ card.spools.length }} {{ card.spools.length === 1 ? 'Spule' : 'Spulen' }}
          <template v-if="card.spools.length"> · {{ grams(card.grams) }}</template>
          <template v-if="card.container.capacity"> · Platz für {{ card.container.capacity }}</template>
        </div>
      </RouterLink>
    </div>

    <ContainerDialog v-model:visible="creating" :container="null" />
  </div>
</template>
