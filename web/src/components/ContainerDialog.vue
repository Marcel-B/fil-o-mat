<script setup lang="ts">
import { ref, watch } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useDataStore } from '@/stores/data'
import type { Container } from '@/types'

const props = defineProps<{ container: Container | null }>()
const visible = defineModel<boolean>('visible', { required: true })
const store = useDataStore()
const toast = useToast()

const name = ref('')
const description = ref('')
const capacity = ref<number | null>(null)
const rfidTag = ref('')
const saving = ref(false)

watch(visible, (open) => {
  if (!open) return
  name.value = props.container?.name ?? ''
  description.value = props.container?.description ?? ''
  capacity.value = props.container?.capacity ?? null
  rfidTag.value = ''
})

async function save() {
  saving.value = true
  try {
    await store.saveContainer(props.container?.id ?? null, {
      name: name.value,
      description: description.value,
      capacity: capacity.value,
      rfidTag: rfidTag.value,
    })
    visible.value = false
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Speichern ging nicht', detail: (e as Error).message, life: 6000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal :header="container ? 'Container bearbeiten' : 'Neuer Container'" class="w-[28rem] max-w-[95vw]">
    <div class="flex flex-col gap-4">
      <label class="flex flex-col gap-1 text-sm">Name<InputText v-model="name" autofocus /></label>
      <label class="flex flex-col gap-1 text-sm">Beschreibung<Textarea v-model="description" rows="2" auto-resize /></label>
      <label class="flex flex-col gap-1 text-sm">
        Menge
        <InputNumber locale="de-DE" v-model="capacity" :min="0" fluid placeholder="ohne" />
      </label>
      <label v-if="!container" class="flex flex-col gap-1 text-sm">
        NFC-Tag (UID, optional)
        <InputText v-model="rfidTag" class="font-mono" placeholder="z. B. 04378F53CB2A81" />
        <span class="text-xs text-muted-color">Ohne Tag lässt sich der Container nur hier und in den Listen der Apps wählen, nicht scannen.</span>
      </label>
    </div>
    <template #footer>
      <Button label="Abbrechen" severity="secondary" text @click="visible = false" />
      <Button label="Speichern" icon="pi pi-check" :loading="saving" :disabled="!name.trim()" @click="save" />
    </template>
  </Dialog>
</template>
