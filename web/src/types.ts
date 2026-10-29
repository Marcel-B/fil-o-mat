// Datenformat des Backends (siehe backend/src/Filomat.Api); Zeitpunkte als ISO-Text, außer wo anders vermerkt.

/** Filament-Daten im OpenSpool-Format, wie sie auf dem NFC-Tag stehen. */
export interface FilamentData {
  protocol?: string | null
  version?: string | null
  type?: string | null
  color_hex?: string | null
  brand?: string | null
  min_temp?: string | null
  max_temp?: string | null
  bed_min_temp?: string | null
  bed_max_temp?: string | null
  variant?: string | null
  color?: string | null
}

/** Eine Filamentsorte (Tag auf dem Musterplättchen). */
export interface Item {
  id: string
  rfidTag: string
  extraTags: string[]
  name: string
  description: string
  createdAt: string
  containerId: string | null
  itemData: FilamentData | null
  netWeight: number | null
  spoolWeight: number | null
  spoolsInStock: number | null
  transmissionDistance: number | null
}

/** Angebrochene Spule einer Sorte; die Nummer steht mit Stift auf der Spule. */
export interface Spool {
  id: string
  itemId: string
  number: number
  remainingWeight: number | null
  printerId: string | null
  tool: number | null
  containerId: string | null
  openedAt: string
  weighedAt: string | null
  emptiedAt: string | null
}

export interface Container {
  id: string
  rfidTag: string
  name: string
  description: string
  capacity: number | null
  createdAt: string
  isStandard: boolean
}

export type PrinterType = 'Moonraker' | 'SnapmakerU1' | 'PrusaLink' | 'Anycubic' | 'Bambu'

export interface Printer {
  id: string
  name: string
  type: PrinterType
  baseUrl: string
  hasApiKey: boolean
  enabled: boolean
  containerId: string
  tools: number
  createdAt: string
  lastJobEndedAt: string | null
  lastSyncAt: string | null
  lastSyncError: string | null
  readerSeenAt: string | null
}

export type PrintState = 'Idle' | 'Printing' | 'Paused' | 'Finished' | 'Cancelled' | 'Error' | 'Offline'

export interface Heater {
  name: string
  tool: number | null
  actual: number | null
  target: number | null
}

export interface SlotReport {
  tool: number
  loaded: boolean | null
  type: string | null
  colorHex: string | null
  remainPercent: number | null
  tagUid: string | null
}

export interface DryerReport {
  name: string
  humidityPercent: number | null
  humidityLevel: number | null
  temperature: number | null
  tools: number[]
}

export interface PrinterStatus {
  state: PrintState
  jobName: string | null
  jobKey: string | null
  progress: number | null
  remainingSeconds: number | null
  elapsedSeconds: number | null
  layer: number | null
  totalLayers: number | null
  activeTool: number | null
  heaters: Heater[] | null
  slots: SlotReport[] | null
  dryers: DryerReport[] | null
  jobGramsPerTool: number[] | null
  jobGramsEstimated: boolean
  message: string | null
  hasThumbnail: boolean
  isActive: boolean
}

export interface ToolLive {
  tool: number
  place: string
  spoolId: string | null
  spoolNumber: number | null
  itemId: string | null
  itemName: string | null
  type: string | null
  colorHex: string | null
  remainingGrams: number | null
  reported: SlotReport | null
  neededGrams: number | null
  enough: boolean | null
}

export interface Hint {
  kind: string
  warning: boolean
  tool: number | null
  text: string
}

export interface PrinterLive {
  printerId: string
  name: string
  type: PrinterType
  updatedAt: string | null
  status: PrinterStatus
  tools: ToolLive[]
  hints: Hint[]
}

export interface Consumption {
  id: string
  printerId: string
  jobId: string
  jobName: string
  status: string
  endedAt: string
  lengthMm: number
  grams: number
  tool: number
  tagUid: string | null
  spoolId: string | null
  itemId: string | null
  recordedAt: string
  durationSeconds: number | null
}

export interface StatsTotals {
  grams: number
  jobs: number
  completed: number
  cancelled: number
  printSeconds: number
  cancelledGrams: number
}

export interface StatsResponse {
  from: string
  totals: StatsTotals
  months: { month: string; grams: number; jobs: number; printSeconds: number }[]
  printers: { printerId: string; name: string; totals: StatsTotals }[]
  items: { itemId: string | null; name: string; type: string | null; colorHex: string | null; grams: number; jobs: number }[]
  types: { type: string; grams: number }[]
}

export interface NotificationSettings {
  url: string | null
  hasToken: boolean
  fromApp: boolean
  configured: boolean
}

/** Zeiten in Millisekunden seit 1970. */
export interface BackupStatus {
  configured: boolean
  running: boolean
  last: {
    startedAt: number
    finishedAt: number
    success: boolean
    archives: string[]
    bytes: number
    error: string | null
    scheduled: boolean
  } | null
  nextRun: number | null
}

/** Zeiten in Millisekunden seit 1970. */
export interface UpdateStatus {
  supported: boolean
  branch: string | null
  currentVersion: string | null
  currentDate: number | null
  latestVersion: string | null
  updateAvailable: boolean
  commits: { sha: string; subject: string; date: number }[]
  checkedAt: number | null
  state: 'idle' | 'requested' | 'running' | 'failed'
  message: string | null
}

export interface VersionInfo {
  apiLevel: number
  minAppLevel: number
  commit: string | null
}

/** Platz einer Spule: Kopf eines Druckers oder Lager, dort optional ein Container. */
export interface SpoolPlace {
  printerId: string | null
  tool: number | null
  containerId: string | null
}
