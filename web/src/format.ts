import { intlLocale, t } from './i18n'
import type { Item, Printer, PrinterType, SpoolPlace, Container } from './types'

// Zahlen und Datum im Format der gewählten Sprache
const number = () => new Intl.NumberFormat(intlLocale(), { maximumFractionDigits: 1 })

export function grams(value: number | null | undefined): string {
  if (value == null) return '–'
  return value >= 1000 ? `${number().format(value / 1000)} kg` : `${Math.round(value)} g`
}

export function decimal(value: number | null | undefined, digits = 1): string {
  if (value == null) return '–'
  return value.toLocaleString(intlLocale(), { maximumFractionDigits: digits })
}

/** "Prusament PLA Matte", ohne Angaben der Name. Wie displayTitle in den Apps. */
export function displayTitle(item: Item | null | undefined): string {
  if (!item) return t('common.filament')
  const d = item.itemData
  const parts = [d?.brand, d?.type, d?.variant].map((p) => p?.trim()).filter((p): p is string => !!p)
  return parts.length ? parts.join(' ') : item.name
}

/** Gleiche Marke, Typ, Variante und Farbe ergeben denselben Schlüssel; null ohne Marke oder Typ. */
export function duplicateKey(item: Item): string | null {
  const n = (v: string | null | undefined) => (v ?? '').trim().toLowerCase()
  const d = item.itemData
  const brand = n(d?.brand)
  const type = n(d?.type)
  if (!brand || !type) return null
  const color = n(d?.color_hex).replace('#', '')
  return [brand, type, n(d?.variant), color || n(d?.color)].join('|')
}

/** "#RRGGBB" für CSS; null, wenn keine gültige Farbe. Achtstellig ist AARRGGBB wie bei Android (wie HexColor in iOS). */
export function cssColor(hex: string | null | undefined): string | null {
  const value = (hex ?? '').trim().replace(/^#/, '')
  if (/^[0-9a-fA-F]{6}$/.test(value)) return `#${value}`
  if (/^[0-9a-fA-F]{8}$/.test(value)) return `#${value.slice(2)}`
  return null
}

/** "Slot" bei Druckern mit Spulenwechsler, sonst "Kopf"; leer bei einem Kopf (wie PlaceName im Backend). */
export function placeName(type: PrinterType, tools: number, tool: number): string {
  if (tools <= 1) return ''
  const slots = type === 'PrusaLink' || type === 'Anycubic' || type === 'Bambu'
  return t(slots ? 'format.slot' : 'format.head', { n: tool + 1 })
}

export function printerPlace(printer: Printer, tool: number | null): string {
  const place = placeName(printer.type, printer.tools, tool ?? 0)
  return place ? `${printer.name} ${place}` : printer.name
}

/** "U1 Kopf 2", "Regal oben" oder "Lager". */
export function placeText(place: SpoolPlace, printers: Printer[], containers: Container[]): string {
  if (place.printerId) {
    const printer = printers.find((p) => p.id === place.printerId)
    return printer ? printerPlace(printer, place.tool) : t('common.printer')
  }
  if (place.containerId) {
    const container = containers.find((c) => c.id === place.containerId)
    if (container) return container.name
  }
  return t('common.storage')
}

export function duration(seconds: number | null | undefined): string {
  if (seconds == null) return '–'
  const h = Math.floor(seconds / 3600)
  const m = Math.round((seconds % 3600) / 60)
  return h > 0 ? `${h} h ${m} min` : `${m} min`
}


export function formatDateTime(value: string | number | null | undefined): string {
  if (value == null) return '–'
  return new Intl.DateTimeFormat(intlLocale(), { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

export function formatDate(value: string | number | null | undefined): string {
  if (value == null) return '–'
  return new Intl.DateTimeFormat(intlLocale(), { dateStyle: 'medium' }).format(new Date(value))
}

/** "vor 3 min", "vor 2 h", sonst Datum. */
export function ago(value: string | number | null | undefined): string {
  if (value == null) return t('format.never')
  const seconds = (Date.now() - new Date(value).getTime()) / 1000
  if (seconds < 60) return t('format.justNow')
  if (seconds < 3600) return t('format.minutesAgo', { n: Math.round(seconds / 60) })
  if (seconds < 86400) return t('format.hoursAgo', { n: Math.round(seconds / 3600) })
  return formatDateTime(value)
}

/** Text aus einem Eingabefeld als Zahl; leer oder ungültig ergibt null. Komma oder Punkt. */
export function parseNumber(text: string | number | null | undefined): number | null {
  if (text == null) return null
  if (typeof text === 'number') return Number.isFinite(text) ? text : null
  const value = Number(text.trim().replace(',', '.'))
  return text.trim() === '' || !Number.isFinite(value) ? null : value
}
