import { createI18n } from 'vue-i18n'
import de from './locales/de'
import en from './locales/en'

export type Locale = 'de' | 'en'
export type LanguageChoice = Locale | 'system'

const storage = 'filomat.language'

/** Sprache des Browsers: Deutsch, wenn er Deutsch bevorzugt, sonst Englisch. */
export function browserLocale(): Locale {
  const preferred = navigator.languages?.length ? navigator.languages : [navigator.language]
  for (const lang of preferred) {
    const code = lang.toLowerCase().slice(0, 2)
    if (code === 'de' || code === 'en') return code
  }
  return 'en'
}

export function storedLanguage(): LanguageChoice {
  try {
    const value = localStorage.getItem(storage)
    return value === 'de' || value === 'en' ? value : 'system'
  } catch {
    return 'system'
  }
}

export const i18n = createI18n<[typeof de], Locale, false>({
  legacy: false,
  locale: storedLanguage() === 'system' ? browserLocale() : (storedLanguage() as Locale),
  fallbackLocale: 'de',
  messages: { de, en },
})

document.documentElement.lang = i18n.global.locale.value

/** Für Code außerhalb von Komponenten (Formatierung, Store). */
export const t = i18n.global.t

export function setLanguage(choice: LanguageChoice) {
  try {
    if (choice === 'system') localStorage.removeItem(storage)
    else localStorage.setItem(storage, choice)
  } catch {
    // Ohne Speicher gilt die Wahl bis zum Neuladen
  }
  const locale = choice === 'system' ? browserLocale() : choice
  i18n.global.locale.value = locale
  document.documentElement.lang = locale
}

/** Gebietsschema für Zahlen und Datum zur gewählten Sprache. */
export function intlLocale(): string {
  return i18n.global.locale.value === 'de' ? 'de-DE' : 'en-GB'
}
