import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import Components from 'unplugin-vue-components/vite'
import { PrimeVueResolver } from '@primevue/auto-import-resolver'

// Beim Entwickeln gehen /api-Aufrufe an ein laufendes Backend (Standard: lokal auf Port 8080)
const backend = process.env.FILOMAT_BACKEND ?? 'http://localhost:8080'

export default defineConfig({
  plugins: [vue(), tailwindcss(), Components({ dts: 'src/components.d.ts', resolvers: [PrimeVueResolver()] })],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    proxy: { '/api': backend },
  },
  build: {
    chunkSizeWarningLimit: 1500,
  },
})
