import type { ItemFormState } from './components/ItemForm.vue'
import type { ItemInput } from './stores/data'
import type { Item } from './types'

export function emptyForm(): ItemFormState {
  return {
    type: '',
    brand: '',
    variant: '',
    colorHex: '',
    color: '',
    minTemp: '',
    maxTemp: '',
    bedMinTemp: '',
    bedMaxTemp: '',
    netWeight: 1000,
    spoolWeight: null,
    spoolsInStock: 0,
    transmissionDistance: null,
  }
}

export function formFromItem(item: Item): ItemFormState {
  const d = item.itemData ?? {}
  return {
    type: d.type ?? '',
    brand: d.brand ?? '',
    variant: d.variant ?? '',
    colorHex: d.color_hex ?? '',
    color: d.color ?? '',
    minTemp: d.min_temp ?? '',
    maxTemp: d.max_temp ?? '',
    bedMinTemp: d.bed_min_temp ?? '',
    bedMaxTemp: d.bed_max_temp ?? '',
    netWeight: item.netWeight,
    spoolWeight: item.spoolWeight,
    spoolsInStock: item.spoolsInStock ?? 0,
    transmissionDistance: item.transmissionDistance,
  }
}

const value = (s: string) => s.trim() || null

/** Wie filamentData in der iOS-App: Farbe ohne "#", Protokoll und Version bleiben erhalten. */
export function inputFromForm(form: ItemFormState, existing?: Item): ItemInput {
  return {
    itemData: {
      protocol: existing?.itemData?.protocol ?? 'openspool',
      version: existing?.itemData?.version ?? '1.0',
      type: value(form.type),
      color_hex: value(form.colorHex)?.replace(/^#/, '') ?? null,
      brand: value(form.brand),
      min_temp: value(form.minTemp),
      max_temp: value(form.maxTemp),
      bed_min_temp: value(form.bedMinTemp),
      bed_max_temp: value(form.bedMaxTemp),
      variant: value(form.variant),
      color: value(form.color),
    },
    netWeight: form.netWeight,
    spoolWeight: form.spoolWeight,
    // Ohne geführten Vorrat bleibt es dabei, solange niemand einen einträgt
    spoolsInStock: existing?.spoolsInStock == null && form.spoolsInStock === 0 ? null : form.spoolsInStock,
    transmissionDistance: form.transmissionDistance,
  }
}

/** Name für neue Sorten wie in den Apps: "<Marke> <Typ>". */
export function defaultName(form: ItemFormState): string {
  return `${form.brand.trim()} ${form.type.trim()}`.trim() || 'Filament'
}
