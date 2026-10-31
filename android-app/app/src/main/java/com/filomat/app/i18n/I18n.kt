package com.filomat.app.i18n

import java.util.Locale

/**
 * Übersetzung der Oberfläche. Die Texte stehen auf Deutsch im Code und sind zugleich der Schlüssel
 * für die englische Fassung in [English] (wie in der iOS-App mit `Localizable.xcstrings`).
 * Ohne Aufruf von [use] bleibt alles deutsch, auch in den Unit-Tests.
 */
object I18n {
    @Volatile
    var english: Boolean = false
        private set

    /** Sprache der Oberfläche übernehmen, z. B. aus `resources.configuration` der Activity. */
    fun use(locale: Locale) {
        english = locale.language != "de"
    }
}

/** Deutscher Text, in der englischen Oberfläche übersetzt. */
fun tr(de: String): String = if (I18n.english) English.texts[de] ?: de else de

/** Wie [tr], mit Platzhaltern (`%s`, in der Übersetzung auch `%1$s`) wie bei [String.format]. */
fun tr(de: String, vararg args: Any?): String = String.format(tr(de), *args)
