<script setup lang="ts">
import { onUnmounted, ref, watch } from 'vue'
import { api } from '@/api'

const props = defineProps<{ printerId: string; jobKey: string | null }>()
const url = ref<string | null>(null)

// Neu laden, sobald ein anderer Druck läuft; über fetch, damit der API-Schlüssel mitgeht
watch(
  () => [props.printerId, props.jobKey],
  async () => {
    const blob = props.jobKey ? await api.blob(`/printers/${props.printerId}/thumbnail`) : null
    if (url.value) URL.revokeObjectURL(url.value)
    url.value = blob ? URL.createObjectURL(blob) : null
  },
  { immediate: true },
)

onUnmounted(() => url.value && URL.revokeObjectURL(url.value))
</script>

<template>
  <img v-if="url" :src="url" alt="Vorschau" class="h-24 w-24 shrink-0 rounded-lg bg-surface-100 object-contain dark:bg-surface-800" />
</template>
