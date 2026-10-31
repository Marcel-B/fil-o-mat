<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useDataStore } from '@/stores/data'
import { intlLocale } from '@/i18n'
import type { Container } from '@/types'

const props = defineProps<{ container: Container | null }>()
const visible = defineModel<boolean>('visible', { required: true })
const store = useDataStore()
const toast = useToast()
const { t } = useI18n()

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
    toast.add({ severity: 'error', summary: t('common.saveFailed'), detail: (e as Error).message, life: 6000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal :header="container ? t('containers.editTitle') : t('containers.new')" class="w-[28rem] max-w-[95vw]">
    <div class="flex flex-col gap-4">
      <label class="flex flex-col gap-1 text-sm">{{ t('common.name') }}<InputText v-model="name" autofocus /></label>
      <label class="flex flex-col gap-1 text-sm">{{ t('common.description') }}<Textarea v-model="description" rows="2" auto-resize /></label>
      <label class="flex flex-col gap-1 text-sm">
        {{ t('common.quantity') }}
        <InputNumber :locale="intlLocale()" v-model="capacity" :min="0" fluid :placeholder="t('common.none')" />
      </label>
      <label v-if="!container" class="flex flex-col gap-1 text-sm">
        {{ t('containers.tag') }}
        <InputText v-model="rfidTag" class="font-mono" :placeholder="t('containers.tagPlaceholder')" />
        <span class="text-xs text-muted-color">{{ t('containers.tagHint') }}</span>
      </label>
    </div>
    <template #footer>
      <Button :label="t('common.cancel')" severity="secondary" text @click="visible = false" />
      <Button :label="t('common.save')" icon="pi pi-check" :loading="saving" :disabled="!name.trim()" @click="save" />
    </template>
  </Dialog>
</template>
