<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { usePrimeVue } from 'primevue/config'
import { useDataStore } from '@/stores/data'
import { primeLocales } from '@/locales/primevue'

const store = useDataStore()
const route = useRoute()
const menuOpen = ref(false)
const { t, locale } = useI18n()

// Texte in PrimeVue-Komponenten folgen der gewählten Sprache
const primevue = usePrimeVue()
watch(locale, (l) => Object.assign(primevue.config.locale!, primeLocales[l as 'de' | 'en']))

const nav = [
  { to: '/drucker', label: 'nav.printers', icon: 'pi pi-print' },
  { to: '/filamente', label: 'nav.filaments', icon: 'pi pi-palette' },
  { to: '/spulen', label: 'nav.spools', icon: 'pi pi-circle' },
  { to: '/container', label: 'nav.containers', icon: 'pi pi-box' },
  { to: '/statistik', label: 'nav.stats', icon: 'pi pi-chart-bar' },
  { to: '/einstellungen', label: 'nav.settings', icon: 'pi pi-cog' },
]

// Wie die Apps: alle zwei Minuten und beim Zurückkommen neu laden
let timer: number | undefined
const onVisible = () => {
  if (document.visibilityState === 'visible') store.refresh()
}

onMounted(() => {
  store.refresh()
  timer = window.setInterval(() => document.visibilityState === 'visible' && store.refresh(), 120_000)
  document.addEventListener('visibilitychange', onVisible)
})

onUnmounted(() => {
  window.clearInterval(timer)
  document.removeEventListener('visibilitychange', onVisible)
})
</script>

<template>
  <div class="min-h-screen lg:flex">
    <aside
      class="hidden w-60 shrink-0 flex-col border-r border-surface-200 bg-surface-0 lg:flex dark:border-surface-800 dark:bg-surface-900"
    >
      <RouterLink to="/" class="flex items-center gap-3 px-5 py-5">
        <img src="/favicon.svg" alt="" class="h-9 w-9 rounded-lg" />
        <span class="text-lg font-semibold">fil-O-mat</span>
      </RouterLink>
      <nav class="flex flex-col gap-1 px-3">
        <RouterLink
          v-for="entry in nav"
          :key="entry.to"
          :to="entry.to"
          class="flex items-center gap-3 rounded-lg px-3 py-2 text-surface-700 hover:bg-surface-100 dark:text-surface-200 dark:hover:bg-surface-800"
          active-class="!bg-primary-100 !text-primary-800 dark:!bg-primary-900/40 dark:!text-primary-200"
        >
          <i :class="entry.icon" />
          {{ t(entry.label) }}
        </RouterLink>
      </nav>
    </aside>

    <header
      class="sticky top-0 z-10 flex items-center gap-3 border-b border-surface-200 bg-surface-0 px-4 py-2 lg:hidden dark:border-surface-800 dark:bg-surface-900"
    >
      <Button icon="pi pi-bars" text rounded :aria-label="t('nav.menu')" @click="menuOpen = true" />
      <img src="/favicon.svg" alt="" class="h-7 w-7 rounded-md" />
      <span class="font-semibold">{{ route.meta.title ? t(route.meta.title as string) : 'fil-O-mat' }}</span>
    </header>

    <Drawer v-model:visible="menuOpen" header="fil-O-mat" class="!w-64">
      <nav class="flex flex-col gap-1">
        <RouterLink
          v-for="entry in nav"
          :key="entry.to"
          :to="entry.to"
          class="flex items-center gap-3 rounded-lg px-3 py-2 hover:bg-surface-100 dark:hover:bg-surface-800"
          active-class="!bg-primary-100 !text-primary-800 dark:!bg-primary-900/40 dark:!text-primary-200"
          @click="menuOpen = false"
        >
          <i :class="entry.icon" />
          {{ t(entry.label) }}
        </RouterLink>
      </nav>
    </Drawer>

    <main class="min-w-0 flex-1 p-4 lg:p-8">
      <Message v-if="store.error" severity="error" class="mb-4" :closable="false">
        {{ t('app.backendUnreachable', { error: store.error }) }}
        <RouterLink v-if="store.unauthorized" to="/einstellungen" class="ml-1 underline">
          {{ t('app.enterKey') }}
        </RouterLink>
      </Message>
      <RouterView />
    </main>

    <Toast position="bottom-right" />
    <ConfirmDialog />
  </div>
</template>
