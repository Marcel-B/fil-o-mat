import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import Tooltip from 'primevue/tooltip'
import Aura from '@primeuix/themes/aura'
import { definePreset } from '@primeuix/themes'
import 'primeicons/primeicons.css'
import './style.css'
import App from './App.vue'
import { router } from './router'

// Messing wie im App-Symbol
const Filomat = definePreset(Aura, {
  semantic: {
    primary: {
      50: '{amber.50}',
      100: '{amber.100}',
      200: '{amber.200}',
      300: '{amber.300}',
      400: '{amber.400}',
      500: '{amber.500}',
      600: '{amber.600}',
      700: '{amber.700}',
      800: '{amber.800}',
      900: '{amber.900}',
      950: '{amber.950}',
    },
  },
})

createApp(App)
  .use(createPinia())
  .use(router)
  .use(PrimeVue, {
    theme: {
      preset: Filomat,
      // Dunkel, wenn das System dunkel ist; die Klasse auf <html> richtet App.vue danach
      options: { darkModeSelector: '.dark', cssLayer: { name: 'primevue', order: 'theme, base, primevue' } },
    },
    locale: {
      accept: 'Ja',
      reject: 'Nein',
      cancel: 'Abbrechen',
      choose: 'Wählen',
      clear: 'Leeren',
      apply: 'Übernehmen',
      emptyMessage: 'Keine Einträge',
      emptyFilterMessage: 'Nichts gefunden',
      emptySearchMessage: 'Nichts gefunden',
      searchMessage: '{0} Treffer',
      selectionMessage: '{0} ausgewählt',
      emptySelectionMessage: 'Nichts ausgewählt',
    },
  })
  .use(ToastService)
  .use(ConfirmationService)
  .directive('tooltip', Tooltip)
  .mount('#app')
