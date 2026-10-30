<script setup lang="ts">
import { computed } from 'vue'
import { cssColor } from '@/format'

const props = withDefaults(defineProps<{ hex?: string | null; size?: number }>(), { hex: null, size: 16 })
const color = computed(() => cssColor(props.hex))
</script>

<template>
  <span
    class="inline-block shrink-0 rounded-full border border-surface-300 dark:border-surface-600"
    :class="{ 'bg-[repeating-linear-gradient(45deg,transparent_0_3px,var(--p-surface-300)_3px_5px)]': !color }"
    :style="{ width: `${size}px`, height: `${size}px`, background: color ?? undefined }"
    :title="color ?? 'Keine Farbe'"
  />
</template>
