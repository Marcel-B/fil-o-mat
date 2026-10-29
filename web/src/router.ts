import { createRouter, createWebHistory } from 'vue-router'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/drucker' },
    { path: '/drucker', name: 'printers', component: () => import('./views/PrintersView.vue'), meta: { title: 'Drucker' } },
    { path: '/filamente', name: 'items', component: () => import('./views/ItemsView.vue'), meta: { title: 'Filamente' } },
    { path: '/filamente/:id', name: 'item', component: () => import('./views/ItemDetailView.vue'), props: true, meta: { title: 'Filament' } },
    { path: '/spulen', name: 'spools', component: () => import('./views/SpoolsView.vue'), meta: { title: 'Spulen' } },
    { path: '/container', name: 'containers', component: () => import('./views/ContainersView.vue'), meta: { title: 'Container' } },
    { path: '/container/:id', name: 'container', component: () => import('./views/ContainerDetailView.vue'), props: true, meta: { title: 'Container' } },
    { path: '/statistik', name: 'stats', component: () => import('./views/StatsView.vue'), meta: { title: 'Statistik' } },
    { path: '/einstellungen', name: 'settings', component: () => import('./views/SettingsView.vue'), meta: { title: 'Einstellungen' } },
    { path: '/:pathMatch(.*)*', redirect: '/drucker' },
  ],
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · fil-O-mat` : 'fil-O-mat'
})
