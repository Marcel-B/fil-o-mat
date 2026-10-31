import { ref } from 'vue'

export type ThemeChoice = 'system' | 'light' | 'dark'

const storage = 'filomat.theme'
const media = window.matchMedia('(prefers-color-scheme: dark)')

function stored(): ThemeChoice {
  try {
    const value = localStorage.getItem(storage)
    return value === 'light' || value === 'dark' ? value : 'system'
  } catch {
    return 'system'
  }
}

export const theme = ref<ThemeChoice>(stored())
/** Ob gerade dunkel dargestellt wird (für Diagramme, die keine CSS-Variablen lesen). */
export const isDark = ref(false)

function apply() {
  isDark.value = theme.value === 'dark' || (theme.value === 'system' && media.matches)
  document.documentElement.classList.toggle('dark', isDark.value)
}

export function setTheme(choice: ThemeChoice) {
  try {
    if (choice === 'system') localStorage.removeItem(storage)
    else localStorage.setItem(storage, choice)
  } catch {
    // Ohne Speicher gilt die Wahl bis zum Neuladen
  }
  theme.value = choice
  apply()
}

media.addEventListener('change', apply)
apply()
