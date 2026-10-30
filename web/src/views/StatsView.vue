<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { api } from '@/api'
import { decimal, duration, grams } from '@/format'
import ColorDot from '@/components/ColorDot.vue'
import type { StatsResponse } from '@/types'

const months = ref(12)
const monthOptions = [
  { label: '6 Monate', value: 6 },
  { label: '12 Monate', value: 12 },
  { label: '24 Monate', value: 24 },
]
const stats = ref<StatsResponse | null>(null)
const error = ref<string | null>(null)

async function load() {
  try {
    stats.value = await api.get<StatsResponse>(`/stats?months=${months.value}`)
    error.value = null
  } catch (e) {
    error.value = (e as Error).message
  }
}
onMounted(load)
watch(months, load)

const monthName = new Intl.DateTimeFormat('de-DE', { month: 'short', year: '2-digit' })

// Ein Wert pro Monat, eine Farbe: die Länge trägt die Menge, Werte stehen im Tooltip
const dark = window.matchMedia('(prefers-color-scheme: dark)').matches
const chartData = computed(() => ({
  labels: stats.value?.months.map((m) => monthName.format(new Date(`${m.month}-01T00:00:00`))) ?? [],
  datasets: [
    {
      label: 'Verbrauch',
      data: stats.value?.months.map((m) => Math.round(m.grams)) ?? [],
      backgroundColor: dark ? '#fbbf24' : '#d97706',
      borderRadius: 4,
      maxBarThickness: 32,
    },
  ],
}))
const chartOptions = computed(() => {
  const ink = dark ? '#a1a1aa' : '#71717a'
  const grid = dark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.06)'
  return {
    maintainAspectRatio: false,
    plugins: {
      legend: { display: false },
      tooltip: {
        callbacks: {
          label: (ctx: { dataIndex: number }) => {
            const m = stats.value!.months[ctx.dataIndex]
            return [`${grams(m.grams)}`, `${m.jobs} Drucke`, duration(m.printSeconds)]
          },
        },
      },
    },
    scales: {
      x: { grid: { display: false }, ticks: { color: ink } },
      y: { beginAtZero: true, grid: { color: grid }, border: { display: false }, ticks: { color: ink, callback: (v: number) => grams(v) } },
    },
  }
})

const maxPrinter = computed(() => Math.max(1, ...(stats.value?.printers.map((p) => p.totals.grams) ?? [])))
const maxItem = computed(() => Math.max(1, ...(stats.value?.items.map((i) => i.grams) ?? [])))
const maxType = computed(() => Math.max(1, ...(stats.value?.types.map((t) => t.grams) ?? [])))
</script>

<template>
  <div class="mx-auto flex max-w-6xl flex-col gap-6">
    <div class="flex flex-wrap items-center gap-3">
      <h1 class="text-2xl font-semibold">Statistik</h1>
      <span class="flex-1" />
      <SelectButton v-model="months" :options="monthOptions" option-label="label" option-value="value" :allow-empty="false" />
    </div>

    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>

    <template v-if="stats">
      <div class="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <div class="rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900">
          <div class="text-sm text-muted-color">Verbrauch</div>
          <div class="text-3xl font-semibold">{{ grams(stats.totals.grams) }}</div>
        </div>
        <div class="rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900">
          <div class="text-sm text-muted-color">Drucke</div>
          <div class="text-3xl font-semibold">{{ stats.totals.jobs }}</div>
          <div class="text-xs text-muted-color">{{ stats.totals.completed }} fertig</div>
        </div>
        <div class="rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900">
          <div class="text-sm text-muted-color">Abgebrochen</div>
          <div class="text-3xl font-semibold">{{ stats.totals.cancelled }}</div>
          <div class="text-xs text-muted-color">{{ grams(stats.totals.cancelledGrams) }} Filament</div>
        </div>
        <div class="rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900">
          <div class="text-sm text-muted-color">Druckzeit</div>
          <div class="text-3xl font-semibold">{{ decimal(stats.totals.printSeconds / 3600, 0) }} h</div>
        </div>
      </div>

      <Card>
        <template #title>Verbrauch pro Monat</template>
        <template #content>
          <Chart type="bar" :data="chartData" :options="chartOptions" class="h-72" />
        </template>
      </Card>

      <div class="grid gap-6 lg:grid-cols-2">
        <Card>
          <template #title>Drucker</template>
          <template #content>
            <ul class="flex flex-col gap-3">
              <li v-for="p in stats.printers" :key="p.printerId" class="flex flex-col gap-1">
                <div class="flex justify-between text-sm">
                  <span>{{ p.name }}</span>
                  <span>{{ grams(p.totals.grams) }} · {{ p.totals.jobs }} Drucke</span>
                </div>
                <div class="h-2 rounded-full bg-surface-100 dark:bg-surface-800">
                  <div class="h-2 rounded-full bg-primary" :style="{ width: `${(p.totals.grams / maxPrinter) * 100}%` }" />
                </div>
              </li>
              <li v-if="!stats.printers.length" class="text-muted-color">Noch keine Drucke</li>
            </ul>
          </template>
        </Card>
        <Card>
          <template #title>Material</template>
          <template #content>
            <ul class="flex flex-col gap-3">
              <li v-for="t in stats.types" :key="t.type" class="flex flex-col gap-1">
                <div class="flex justify-between text-sm"><span>{{ t.type }}</span><span>{{ grams(t.grams) }}</span></div>
                <div class="h-2 rounded-full bg-surface-100 dark:bg-surface-800">
                  <div class="h-2 rounded-full bg-primary" :style="{ width: `${(t.grams / maxType) * 100}%` }" />
                </div>
              </li>
            </ul>
          </template>
        </Card>
      </div>

      <Card>
        <template #title>Sorten</template>
        <template #content>
          <ul class="flex flex-col gap-3">
            <li v-for="i in stats.items" :key="i.itemId ?? 'none'" class="flex items-center gap-3">
              <ColorDot :hex="i.colorHex" :size="18" />
              <div class="flex min-w-0 flex-1 flex-col gap-1">
                <div class="flex justify-between gap-2 text-sm">
                  <RouterLink v-if="i.itemId" :to="{ name: 'item', params: { id: i.itemId } }" class="truncate hover:underline">{{ i.name }}</RouterLink>
                  <span v-else class="truncate text-muted-color">{{ i.name }}</span>
                  <span class="shrink-0">{{ grams(i.grams) }} · {{ i.jobs }} Drucke</span>
                </div>
                <div class="h-2 rounded-full bg-surface-100 dark:bg-surface-800">
                  <div class="h-2 rounded-full bg-primary" :style="{ width: `${(i.grams / maxItem) * 100}%` }" />
                </div>
              </div>
            </li>
          </ul>
        </template>
      </Card>
    </template>
    <ProgressSpinner v-else-if="!error" class="mx-auto" />
  </div>
</template>
