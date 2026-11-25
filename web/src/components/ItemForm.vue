<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '@/api'
import { brands, filamentTypes, preset, variants } from '@/presets'
import { cssColor } from '@/format'
import { intlLocale } from '@/i18n'

export interface ItemFormState {
  type: string
  brand: string
  variant: string
  colorHex: string
  color: string
  minTemp: string
  maxTemp: string
  bedMinTemp: string
  bedMaxTemp: string
  netWeight: number | null
  spoolWeight: number | null
  spoolsInStock: number
  transmissionDistance: number | null
}

const form = defineModel<ItemFormState>({ required: true })
/** Beim Anlegen überschreiben die Presets die Temperaturen, beim Bearbeiten füllen sie nur leere Felder. */
const props = defineProps<{ creating?: boolean }>()
const { t } = useI18n()

const typeOptions = computed(() =>
  form.value.type && !filamentTypes.includes(form.value.type) ? [...filamentTypes, form.value.type] : filamentTypes,
)

const brandSuggestions = ref<string[]>([])
const variantSuggestions = ref<string[]>([])
const search = (list: string[], query: string) => {
  const q = query.trim().toLowerCase()
  return q ? list.filter((v) => v.toLowerCase().includes(q)) : list
}

function loadTemperatures() {
  const f = form.value
  const match = f.type ? preset(f.type, f.brand.trim() || null) : undefined
  if (!match) return
  const fill = (current: string, value: string | null | undefined) => (props.creating || !current ? (value ?? current) : current)
  f.minTemp = fill(f.minTemp, match.min_temp)
  f.maxTemp = fill(f.maxTemp, match.max_temp)
  f.bedMinTemp = fill(f.bedMinTemp, match.bed_min_temp)
  f.bedMaxTemp = fill(f.bedMaxTemp, match.bed_max_temp)
}

// Leergewicht der Marke aus der SpoolmanDB vorschlagen, wenn noch keins eingetragen ist
let spoolWeights: Promise<Record<string, number>> | null = null
async function suggestSpoolWeight() {
  const brand = form.value.brand.trim().toLowerCase()
  if (!brand || form.value.spoolWeight != null) return
  spoolWeights ??= api.get<Record<string, number>>('/spool-weights').catch(() => ({}))
  const weights = Object.entries(await spoolWeights)
  const match = weights.find(([k]) => k.toLowerCase() === brand) ?? weights.find(([k]) => k.toLowerCase().startsWith(brand))
  if (match && form.value.spoolWeight == null) form.value.spoolWeight = match[1]
}

// Nur auf Eingaben reagieren, nicht wenn ein anderes Formular (z. B. frisch geladen) hineinkommt
watch(
  () => [form.value, form.value.type] as const,
  ([f], [old]) => f === old && loadTemperatures(),
)
watch(
  () => [form.value, form.value.brand] as const,
  ([f], [old]) => f === old && loadTemperatures(),
)

// Farbwähler arbeitet mit RRGGBB ohne "#"
const pickerColor = computed({
  get: () => cssColor(form.value.colorHex)?.slice(1) ?? 'FFFFFF',
  set: (v: string) => (form.value.colorHex = v.toUpperCase()),
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <section class="grid gap-4 sm:grid-cols-2">
      <label class="flex min-w-0 flex-col gap-1 text-sm">
        {{ t('itemForm.type') }}
        <Select v-model="form.type" :options="typeOptions" editable :placeholder="t('itemForm.choose')" fluid />
      </label>
      <label class="flex min-w-0 flex-col gap-1 text-sm">
        {{ t('itemForm.brand') }}
        <AutoComplete
          v-model="form.brand"
          :suggestions="brandSuggestions"
          dropdown
          fluid
          @complete="brandSuggestions = search(brands, $event.query)"
          @option-select="suggestSpoolWeight"
          @blur="suggestSpoolWeight"
        />
      </label>
      <label class="flex min-w-0 flex-col gap-1 text-sm">
        {{ t('itemForm.variant') }}
        <AutoComplete
          v-model="form.variant"
          :suggestions="variantSuggestions"
          dropdown
          fluid
          @complete="variantSuggestions = search(variants, $event.query)"
        />
      </label>
      <div class="flex min-w-0 flex-col gap-1 text-sm">
        <span>{{ t('itemForm.color') }}</span>
        <div class="flex flex-wrap items-center gap-2">
          <ColorPicker v-model="pickerColor" format="hex" />
          <InputText v-model="form.colorHex" placeholder="FF0000" class="w-28 font-mono" />
          <InputText v-model="form.color" :placeholder="t('itemForm.colorName')" class="min-w-36 flex-1" />
        </div>
      </div>
    </section>

    <section>
      <h3 class="mb-2 text-sm font-semibold text-muted-color">{{ t('itemForm.temperatures') }}</h3>
      <div class="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <label class="flex min-w-0 flex-col gap-1 text-sm">{{ t('itemForm.nozzleMin') }}<InputText v-model="form.minTemp" inputmode="numeric" /></label>
        <label class="flex min-w-0 flex-col gap-1 text-sm">{{ t('itemForm.nozzleMax') }}<InputText v-model="form.maxTemp" inputmode="numeric" /></label>
        <label class="flex min-w-0 flex-col gap-1 text-sm">{{ t('itemForm.bedMin') }}<InputText v-model="form.bedMinTemp" inputmode="numeric" /></label>
        <label class="flex min-w-0 flex-col gap-1 text-sm">{{ t('itemForm.bedMax') }}<InputText v-model="form.bedMaxTemp" inputmode="numeric" /></label>
      </div>
    </section>

    <section>
      <h3 class="mb-2 text-sm font-semibold text-muted-color">{{ t('itemForm.kind') }}</h3>
      <div class="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <label class="flex min-w-0 flex-col gap-1 text-sm">
          {{ t('itemForm.netWeight') }}
          <InputNumber :locale="intlLocale()" v-model="form.netWeight" suffix=" g" :min="0" fluid />
        </label>
        <label class="flex min-w-0 flex-col gap-1 text-sm">
          {{ t('itemForm.spoolWeight') }}
          <InputNumber :locale="intlLocale()" v-model="form.spoolWeight" suffix=" g" :min="0" fluid />
        </label>
        <label class="flex min-w-0 flex-col gap-1 text-sm">
          {{ t('itemForm.stock') }}
          <InputNumber :locale="intlLocale()" v-model="form.spoolsInStock" show-buttons :min="0" :max="99" fluid />
        </label>
        <label class="flex min-w-0 flex-col gap-1 text-sm">
          {{ t('itemForm.td') }}
          <InputNumber :locale="intlLocale()" v-model="form.transmissionDistance" suffix=" mm" :min="0" :max-fraction-digits="2" fluid />
        </label>
      </div>
      <p class="mt-2 text-xs text-muted-color">
        {{ t('itemForm.hint') }}
      </p>
    </section>
  </div>
</template>
