<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, getApiKey, setApiKey } from '@/api'
import { useDataStore } from '@/stores/data'
import { ago, formatDateTime } from '@/format'
import type { BackupStatus, NotificationSettings, UpdateStatus, VersionInfo } from '@/types'

const store = useDataStore()
const toast = useToast()
const fail = (summary: string) => (e: unknown) => toast.add({ severity: 'error', summary, detail: (e as Error).message, life: 6000 })

// API-Schlüssel (nur im Browser)
const apiKey = ref(getApiKey())
async function saveKey() {
  setApiKey(apiKey.value.trim())
  await store.refresh()
  if (!store.error) {
    toast.add({ severity: 'success', summary: 'Verbunden', life: 2500 })
    loadAll()
  }
}

// Meldungen
const ntfy = ref<NotificationSettings | null>(null)
const ntfyUrl = ref('')
const ntfyToken = ref('')
const ntfySaving = ref(false)
async function loadNtfy() {
  ntfy.value = await api.get<NotificationSettings>('/settings/notifications')
  ntfyUrl.value = ntfy.value.fromApp ? (ntfy.value.url ?? '') : ''
  ntfyToken.value = ''
}
async function saveNtfy() {
  ntfySaving.value = true
  try {
    ntfy.value = await api.put<NotificationSettings>('/settings/notifications', { url: ntfyUrl.value, token: ntfyToken.value || null })
    ntfyToken.value = ''
    toast.add({ severity: 'success', summary: 'Gespeichert', life: 2500 })
  } catch (e) {
    fail('Speichern ging nicht')(e)
  } finally {
    ntfySaving.value = false
  }
}
async function testNtfy() {
  try {
    await api.post('/settings/notifications/test')
    toast.add({ severity: 'success', summary: 'Testmeldung geschickt', life: 3000 })
  } catch (e) {
    fail('Testmeldung ging nicht')(e)
  }
}

// Sicherung
const backup = ref<BackupStatus | null>(null)
async function runBackup() {
  try {
    backup.value = await api.post<BackupStatus>('/backup')
    toast.add({ severity: 'info', summary: 'Sicherung läuft', life: 3000 })
    setTimeout(async () => (backup.value = await api.get<BackupStatus>('/backup')), 5000)
  } catch (e) {
    fail('Sicherung ging nicht')(e)
  }
}

// Aktualisierung
const update = ref<UpdateStatus | null>(null)
const version = ref<VersionInfo | null>(null)
async function checkUpdate() {
  try {
    update.value = await api.post<UpdateStatus>('/update/check')
    setTimeout(async () => (update.value = await api.get<UpdateStatus>('/update')), 8000)
  } catch (e) {
    fail('Prüfen ging nicht')(e)
  }
}
async function applyUpdate() {
  try {
    update.value = await api.post<UpdateStatus>('/update')
    toast.add({ severity: 'info', summary: 'Update läuft', detail: 'Das Backend startet gleich neu; die Seite lädt danach neu.', life: 8000 })
    waitForRestart()
  } catch (e) {
    fail('Update ging nicht')(e)
  }
}

// Während des Updates nachfragen; ist der neue Stand da, Seite neu laden (neue Oberfläche)
let poll: number | undefined
function waitForRestart() {
  const before = update.value?.currentVersion
  window.clearInterval(poll)
  poll = window.setInterval(async () => {
    try {
      const status = await api.get<UpdateStatus>('/update')
      update.value = status
      if (status.state === 'failed') window.clearInterval(poll)
      if (status.currentVersion !== before && status.state === 'idle') {
        window.clearInterval(poll)
        location.reload()
      }
    } catch {
      // Backend startet gerade neu
    }
  }, 4000)
}
onUnmounted(() => window.clearInterval(poll))

function loadAll() {
  loadNtfy().catch(() => {})
  api.get<BackupStatus>('/backup').then((b) => (backup.value = b), () => {})
  api.get<UpdateStatus>('/update').then((u) => {
    update.value = u
    if (u.state === 'running' || u.state === 'requested') waitForRestart()
  }, () => {})
  api.get<VersionInfo>('/version').then((v) => (version.value = v), () => {})
}
onMounted(loadAll)

const short = (sha: string | null | undefined) => sha?.slice(0, 7) ?? '–'
</script>

<template>
  <div class="mx-auto flex max-w-3xl flex-col gap-6">
    <h1 class="text-2xl font-semibold">Einstellungen</h1>

    <Card>
      <template #title>API-Schlüssel</template>
      <template #subtitle>Nur nötig, wenn im Backend <code>FILOMAT_API_KEY</code> gesetzt ist. Bleibt in diesem Browser gespeichert.</template>
      <template #content>
        <div class="flex gap-2">
          <Password v-model="apiKey" :feedback="false" toggle-mask class="flex-1" fluid placeholder="ohne" />
          <Button label="Speichern" @click="saveKey" />
        </div>
      </template>
    </Card>

    <Card>
      <template #title>Meldungen (ntfy)</template>
      <template #subtitle>
        Fertige und abgebrochene Drucke, Fehler und knappe Spulen. Ein Thema auf ntfy.sh reicht, z. B. https://ntfy.sh/filomat-7f3k2q.
      </template>
      <template #content>
        <div v-if="ntfy" class="flex flex-col gap-3">
          <Message v-if="ntfy.configured && !ntfy.fromApp" severity="info" size="small" :closable="false">
            Aus der Server-Konfiguration: {{ ntfy.url }}. Eine Adresse hier hat Vorrang.
          </Message>
          <label class="flex flex-col gap-1 text-sm">Adresse<InputText v-model="ntfyUrl" placeholder="https://ntfy.sh/mein-thema" /></label>
          <label class="flex flex-col gap-1 text-sm">
            Zugangstoken (eigener Server)
            <Password v-model="ntfyToken" :feedback="false" toggle-mask fluid :placeholder="ntfy?.hasToken ? 'gespeichert, leer lassen zum Behalten' : 'ohne'" />
          </label>
          <div class="flex justify-end gap-2">
            <Button label="Testmeldung" severity="secondary" outlined :disabled="!ntfy?.configured" @click="testNtfy" />
            <Button label="Speichern" :loading="ntfySaving" @click="saveNtfy" />
          </div>
        </div>
      </template>
    </Card>

    <Card>
      <template #title>Sicherung in die Nextcloud</template>
      <template #content>
        <div v-if="backup" class="flex flex-col gap-3 text-sm">
          <template v-if="backup.configured">
            <div v-if="backup.last" class="flex items-center gap-2">
              <i :class="backup.last.success ? 'pi pi-check-circle text-green-500' : 'pi pi-times-circle text-red-500'" />
              Letzte Sicherung {{ formatDateTime(backup.last.finishedAt) }}
              <span v-if="backup.last.error" class="text-red-500">{{ backup.last.error }}</span>
            </div>
            <div v-else class="text-muted-color">Noch keine Sicherung.</div>
            <div v-if="backup.nextRun" class="text-muted-color">Nächste: {{ formatDateTime(backup.nextRun) }}</div>
            <div><Button label="Jetzt sichern" size="small" outlined :loading="backup.running" @click="runBackup" /></div>
          </template>
          <span v-else class="text-muted-color">Nicht eingerichtet (NEXTCLOUD_* in backend/.env, siehe README).</span>
        </div>
      </template>
    </Card>

    <Card>
      <template #title>Version</template>
      <template #content>
        <div class="flex flex-col gap-3 text-sm">
          <div>
            Läuft: <span class="font-mono">{{ short(version?.commit ?? update?.currentVersion) }}</span>
            <span v-if="update?.currentDate" class="text-muted-color"> vom {{ formatDateTime(update.currentDate) }}</span>
            <span v-if="version" class="text-muted-color"> · API-Stufe {{ version.apiLevel }}</span>
          </div>
          <template v-if="update?.supported">
            <div class="text-muted-color">
              Branch {{ update.branch }} · geprüft {{ ago(update.checkedAt) }}
            </div>
            <Message v-if="update.message" :severity="update.state === 'failed' ? 'error' : 'info'" size="small" :closable="false">
              {{ update.message }}
            </Message>
            <div v-if="update.updateAvailable" class="flex flex-col gap-2">
              <div class="font-medium">{{ update.commits.length }} neue Commits:</div>
              <ul class="max-h-60 overflow-auto rounded-lg border border-surface-200 dark:border-surface-800">
                <li v-for="c in update.commits" :key="c.sha" class="flex gap-3 border-b border-surface-100 px-3 py-1.5 last:border-0 dark:border-surface-800">
                  <span class="font-mono text-muted-color">{{ short(c.sha) }}</span>
                  <span class="flex-1">{{ c.subject }}</span>
                </li>
              </ul>
            </div>
            <div v-else class="text-muted-color">Neueste Version.</div>
            <div class="flex gap-2">
              <Button label="Prüfen" size="small" severity="secondary" outlined @click="checkUpdate" />
              <Button
                v-if="update.updateAvailable"
                label="Aktualisieren"
                size="small"
                icon="pi pi-download"
                :loading="update.state === 'running' || update.state === 'requested'"
                @click="applyUpdate"
              />
            </div>
          </template>
          <span v-else-if="update" class="text-muted-color">Kein Updater auf dem Server eingerichtet (deploy/updater/install.sh).</span>
        </div>
      </template>
    </Card>
  </div>
</template>
