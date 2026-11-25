// Dieselben Dateien wie in ios-app/Filomat/Resources und android-app (assets)
import presetList from './presets.json'
import brandList from './brands.json'
import variantList from './variants.json'

export interface FilamentPreset {
  type: string
  brand: string | null
  min_temp: string
  max_temp: string
  bed_min_temp?: string | null
  bed_max_temp?: string | null
}

export const filamentTypes = ['PLA', 'TPU', 'PETG', 'ASA', 'ABS']
export const brands = [...new Set(brandList as string[])].sort((a, b) => a.localeCompare(b, 'de'))
export const variants = [...new Set(variantList as string[])]

export function preset(type: string, brand: string | null): FilamentPreset | undefined {
  const presets = presetList as FilamentPreset[]
  return (brand ? presets.find((p) => p.type === type && p.brand === brand) : undefined) ?? presets.find((p) => p.type === type && !p.brand)
}
