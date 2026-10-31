<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { api, getApiKey, setApiKey } from '@/api'
import { useDataStore } from '@/stores/data'
import { ago, formatDateTime } from '@/format'
import { setLanguage, storedLanguage, type LanguageChoice } from '@/i18n'
import { setTheme, theme, type ThemeChoice } from '@/theme'
import type { BackupStatus, NotificationSettings, UpdateStatus, VersionInfo } from '@/types'

const store = useDataStore()
const toast = useToast()
const { t } = useI18n()
const fail = (summary: string) => (e: unknown) => toast.add({ severity: 'error', summary, detail: (e as Error).message, life: 6000 })

// Sprache und Farbschema (nur in diesem Browser)
const language = ref<LanguageChoice>(storedLanguage())
const languageOptions = computed(() => [
  { label: t('settings.system'), value: 'system' },
  { label: t('settings.german'), value: 'de' },
  { label: t('settings.english'), value: 'en' },
])
const themeChoice = computed({ get: () => theme.value, set: (v: ThemeChoice) => setTheme(v) })
const themeOptions = computed(() => [
  { label: t('settings.system'), value: 'system', icon: 'pi pi-desktop' },
  { label: t('settings.light'), value: 'light', icon: 'pi pi-sun' },
  { label: t('settings.dark'), value: 'dark', icon: 'pi pi-moon' },
])
function chooseLanguage(choice: LanguageChoice) {
  language.value = choice
  setLanguage(choice)
}

// API-Schlüssel (nur im Browser)
const apiKey = ref(getApiKey())
async function saveKey() {
  setApiKey(apiKey.value.trim())
  await store.refresh()
  if (!store.error) {
    toast.add({ severity: 'success', summary: t('settings.connected'), life: 2500 })
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
    toast.add({ severity: 'success', summary: t('common.saved'), life: 2500 })
  } catch (e) {
    fail(t('common.saveFailed'))(e)
  } finally {
    ntfySaving.value = false
  }
}
async function testNtfy() {
  try {
    await api.post('/settings/notifications/test')
    toast.add({ severity: 'success', summary: t('settings.testSent'), life: 3000 })
  } catch (e) {
    fail(t('settings.testFailed'))(e)
  }
}

// Sicherung
const backup = ref<BackupStatus | null>(null)
async function runBackup() {
  try {
    backup.value = await api.post<BackupStatus>('/backup')
    toast.add({ severity: 'info', summary: t('settings.backupRunning'), life: 3000 })
    setTimeout(async () => (backup.value = await api.get<BackupStatus>('/backup')), 5000)
  } catch (e) {
    fail(t('settings.backupFailed'))(e)
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
    fail(t('settings.checkFailed'))(e)
  }
}
async function applyUpdate() {
  try {
    update.value = await api.post<UpdateStatus>('/update')
    toast.add({ severity: 'info', summary: t('settings.updateRunning'), detail: t('settings.updateRunningDetail'), life: 8000 })
    waitForRestart()
  } catch (e) {
    fail(t('settings.updateFailed'))(e)
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
    <h1 class="text-2xl font-semibold">{{ t('settings.title') }}</h1>

    <Card>
      <template #title>{{ t('settings.appearance') }}</template>
      <template #content>
        <div class="flex flex-col gap-4 text-sm">
          <div class="flex flex-col gap-1">
            {{ t('settings.language') }}
            <SelectButton
              :model-value="language"
              :options="languageOptions"
              option-label="label"
              option-value="value"
              :allow-empty="false"
              @update:model-value="chooseLanguage"
            />
          </div>
          <div class="flex flex-col gap-1">
            {{ t('settings.theme') }}
            <SelectButton v-model="themeChoice" :options="themeOptions" option-label="label" option-value="value" :allow-empty="false">
              <template #option="{ option }"><i :class="option.icon" />{{ option.label }}</template>
            </SelectButton>
          </div>
        </div>
      </template>
    </Card>

    <Card>
      <template #title>{{ t('settings.apiKey') }}</template>
      <template #subtitle>
        <i18n-t keypath="settings.apiKeyHint" scope="global"><template #var><code>FILOMAT_API_KEY</code></template></i18n-t>
      </template>
      <template #content>
        <div class="flex gap-2">
          <Password v-model="apiKey" :feedback="false" toggle-mask class="flex-1" fluid :placeholder="t('common.none')" />
          <Button :label="t('common.save')" @click="saveKey" />
        </div>
      </template>
    </Card>

    <Card>
      <template #title>{{ t('settings.ntfy') }}</template>
      <template #subtitle>
        {{ t('settings.ntfyHint') }}
      </template>
      <template #content>
        <div v-if="ntfy" class="flex flex-col gap-3">
          <Message v-if="ntfy.configured && !ntfy.fromApp" severity="info" size="small" :closable="false">
            {{ t('settings.ntfyFromServer', { url: ntfy.url }) }}
          </Message>
          <label class="flex flex-col gap-1 text-sm">{{ t('printers.address') }}<InputText v-model="ntfyUrl" :placeholder="t('settings.ntfyUrlPlaceholder')" /></label>
          <label class="flex flex-col gap-1 text-sm">
            {{ t('settings.ntfyToken') }}
            <Password v-model="ntfyToken" :feedback="false" toggle-mask fluid :placeholder="ntfy?.hasToken ? t('common.keptPlaceholder') : t('common.none')" />
          </label>
          <div class="flex justify-end gap-2">
            <Button :label="t('settings.testMessage')" severity="secondary" outlined :disabled="!ntfy?.configured" @click="testNtfy" />
            <Button :label="t('common.save')" :loading="ntfySaving" @click="saveNtfy" />
          </div>
        </div>
      </template>
    </Card>

    <Card>
      <template #title>{{ t('settings.backup') }}</template>
      <template #content>
        <div v-if="backup" class="flex flex-col gap-3 text-sm">
          <template v-if="backup.configured">
            <div v-if="backup.last" class="flex items-center gap-2">
              <i :class="backup.last.success ? 'pi pi-check-circle text-green-500' : 'pi pi-times-circle text-red-500'" />
              {{ t('settings.lastBackup', { date: formatDateTime(backup.last.finishedAt) }) }}
              <span v-if="backup.last.error" class="text-red-500">{{ backup.last.error }}</span>
            </div>
            <div v-else class="text-muted-color">{{ t('settings.noBackup') }}</div>
            <div v-if="backup.nextRun" class="text-muted-color">{{ t('settings.nextBackup', { date: formatDateTime(backup.nextRun) }) }}</div>
            <div><Button :label="t('settings.backupNow')" size="small" outlined :loading="backup.running" @click="runBackup" /></div>
          </template>
          <span v-else class="text-muted-color">{{ t('settings.backupNotSetUp') }}</span>
        </div>
      </template>
    </Card>

    <Card>
      <template #title>{{ t('settings.version') }}</template>
      <template #content>
        <div class="flex flex-col gap-3 text-sm">
          <div>
            {{ t('settings.running') }} <span class="font-mono">{{ short(version?.commit ?? update?.currentVersion) }}</span>
            <span v-if="update?.currentDate" class="text-muted-color"> {{ t('settings.fromDate', { date: formatDateTime(update.currentDate) }) }}</span>
            <span v-if="version" class="text-muted-color"> · {{ t('settings.apiLevel', { level: version.apiLevel }) }}</span>
          </div>
          <template v-if="update?.supported">
            <div class="text-muted-color">
              {{ t('settings.branch', { branch: update.branch, ago: ago(update.checkedAt) }) }}
            </div>
            <Message v-if="update.message" :severity="update.state === 'failed' ? 'error' : 'info'" size="small" :closable="false">
              {{ update.message }}
            </Message>
            <div v-if="update.updateAvailable" class="flex flex-col gap-2">
              <div class="font-medium">{{ t('settings.newCommits', update.commits.length) }}</div>
              <ul class="max-h-60 overflow-auto rounded-lg border border-surface-200 dark:border-surface-800">
                <li v-for="c in update.commits" :key="c.sha" class="flex gap-3 border-b border-surface-100 px-3 py-1.5 last:border-0 dark:border-surface-800">
                  <span class="font-mono text-muted-color">{{ short(c.sha) }}</span>
                  <span class="flex-1">{{ c.subject }}</span>
                </li>
              </ul>
            </div>
            <div v-else class="text-muted-color">{{ t('settings.latest') }}</div>
            <div class="flex gap-2">
              <Button :label="t('settings.check')" size="small" severity="secondary" outlined @click="checkUpdate" />
              <Button
                v-if="update.updateAvailable"
                :label="t('settings.update')"
                size="small"
                icon="pi pi-download"
                :loading="update.state === 'running' || update.state === 'requested'"
                @click="applyUpdate"
              />
            </div>
          </template>
          <span v-else-if="update" class="text-muted-color">{{ t('settings.noUpdater') }}</span>
        </div>
      </template>
    </Card>
  </div>
</template>
