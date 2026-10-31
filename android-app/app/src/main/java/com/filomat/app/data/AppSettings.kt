package com.filomat.app.data

import android.content.Context
import androidx.appcompat.app.AppCompatDelegate
import androidx.core.os.LocaleListCompat

/** Hell, dunkel oder wie das System; gespeichert in den SharedPreferences, angewendet über AppCompat. */
enum class Appearance(val nightMode: Int) {
    SYSTEM(AppCompatDelegate.MODE_NIGHT_FOLLOW_SYSTEM),
    LIGHT(AppCompatDelegate.MODE_NIGHT_NO),
    DARK(AppCompatDelegate.MODE_NIGHT_YES)
}

/** Sprache der App: wie das Gerät oder fest Deutsch bzw. Englisch. */
enum class AppLanguage(val tag: String) {
    SYSTEM(""),
    GERMAN("de"),
    ENGLISH("en")
}

/** Einstellungen der App selbst (nicht des Backends). */
object AppSettings {
    private const val PREFS = "app"
    private const val KEY_APPEARANCE = "appearance"

    fun appearance(context: Context): Appearance {
        val name = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY_APPEARANCE, null)
        return Appearance.entries.firstOrNull { it.name == name } ?: Appearance.SYSTEM
    }

    fun setAppearance(context: Context, appearance: Appearance) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(KEY_APPEARANCE, appearance.name).apply()
        AppCompatDelegate.setDefaultNightMode(appearance.nightMode)
    }

    /** Beim Start vor dem ersten Zeichnen aufrufen. */
    fun applyAppearance(context: Context) {
        AppCompatDelegate.setDefaultNightMode(appearance(context).nightMode)
    }

    /** Die Sprache speichert AppCompat selbst (bis Android 12 über den Dienst im Manifest, ab 13 das System). */
    fun language(): AppLanguage {
        val tag = AppCompatDelegate.getApplicationLocales().toLanguageTags()
        return AppLanguage.entries.firstOrNull { it.tag.isNotEmpty() && tag.startsWith(it.tag) } ?: AppLanguage.SYSTEM
    }

    fun setLanguage(language: AppLanguage) {
        AppCompatDelegate.setApplicationLocales(LocaleListCompat.forLanguageTags(language.tag))
    }
}
