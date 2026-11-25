import { watch } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import { i18n, t } from './i18n'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/drucker' },
    { path: '/drucker', name: 'printers', component: () => import('./views/PrintersView.vue'), meta: { title: 'nav.printers' } },
    { path: '/filamente', name: 'items', component: () => import('./views/ItemsView.vue'), meta: { title: 'nav.filaments' } },
    { path: '/filamente/:id', name: 'item', component: () => import('./views/ItemDetailView.vue'), props: true, meta: { title: 'nav.item' } },
    { path: '/spulen', name: 'spools', component: () => import('./views/SpoolsView.vue'), meta: { title: 'nav.spools' } },
    { path: '/container', name: 'containers', component: () => import('./views/ContainersView.vue'), meta: { title: 'nav.containers' } },
    { path: '/container/:id', name: 'container', component: () => import('./views/ContainerDetailView.vue'), props: true, meta: { title: 'nav.containers' } },
    { path: '/statistik', name: 'stats', component: () => import('./views/StatsView.vue'), meta: { title: 'nav.stats' } },
    { path: '/einstellungen', name: 'settings', component: () => import('./views/SettingsView.vue'), meta: { title: 'nav.settings' } },
    { path: '/:pathMatch(.*)*', redirect: '/drucker' },
  ],
})

// meta.title ist ein Schlüssel der Übersetzungen
function setTitle() {
  const key = router.currentRoute.value.meta.title as string | undefined
  document.title = key ? `${t(key)} · fil-O-mat` : 'fil-O-mat'
}
router.afterEach(setTitle)
watch(i18n.global.locale, setTitle)
