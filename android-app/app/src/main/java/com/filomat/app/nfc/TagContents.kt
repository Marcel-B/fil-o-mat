package com.filomat.app.nfc

import com.filomat.app.data.ItemJsonData
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull

/** Ein NDEF-Record des Tags, für die Anzeige aufbereitet. */
data class TagRecord(
    /** "MIME", "Well-known", "URI" … */
    val format: String,
    /** MIME-Typ bzw. Record-Typ, z. B. `application/json` oder `T`. */
    val type: String,
    val sizeBytes: Int,
    /** Inhalt als Text, sofern lesbar. */
    val text: String?
)

/** Alles, was beim einfachen Lesen eines Tags herauskommt. */
data class TagReading(
    /** UID als Hex in Großbuchstaben, wie sie die Apps als Tag-ID verwenden. */
    val uid: String,
    /** Technologien, z. B. "NfcA", "MifareUltralight", "Ndef". */
    val technologies: List<String>,
    /** NDEF-Typ des Chips, z. B. "NTAG215" bzw. "org.nfcforum.ndef.type2". */
    val ndefType: String? = null,
    val ndefCapacity: Int? = null,
    val ndefUsed: Int? = null,
    val isWritable: Boolean? = null,
    val records: List<TagRecord> = emptyList(),
    /** Gelesene Filamentdaten, wenn ein JSON-Record vorhanden war. */
    val contents: TagContents? = null,
    val error: String? = null
)

/** Ein Feld aus dem JSON des Tags mit deutscher Bezeichnung, falls bekannt. */
data class TagField(val key: String, val label: String, val value: String)

/** Inhalt eines OpenSpool-Tags (JSON): bekannte Felder mit Bezeichnung, unbekannte so wie sie sind. */
data class TagContents(
    val fields: List<TagField>,
    val itemData: ItemJsonData?,
    val rawJson: String,
    val error: String? = null
) {
    fun value(key: String): String? = fields.firstOrNull { it.key == key }?.value

    companion object {
        /** Bekannte OpenSpool-Felder in Anzeigereihenfolge. `subtype` schreibt z. B. der Snapmaker U1 statt `variant`. */
        val labels: List<Pair<String, String>> = listOf(
            "brand" to "Marke",
            "type" to "Typ",
            "variant" to "Variante",
            "subtype" to "Untertyp",
            "color" to "Farbe",
            "color_hex" to "Farbe (Hex)",
            "min_temp" to "Min. Temperatur (°C)",
            "max_temp" to "Max. Temperatur (°C)",
            "bed_min_temp" to "Min. Bett-Temperatur (°C)",
            "bed_max_temp" to "Max. Bett-Temperatur (°C)",
            "diameter" to "Durchmesser (mm)",
            "weight" to "Gewicht (g)",
            "density" to "Dichte (g/cm³)",
            "protocol" to "Protokoll",
            "version" to "Version",
            // Container-Tags
            "name" to "Name",
            "description" to "Beschreibung",
            "capacity" to "Menge"
        )

        private val json = Json { ignoreUnknownKeys = true; isLenient = true }

        fun parse(text: String): TagContents {
            val element = try {
                json.parseToJsonElement(text)
            } catch (e: Exception) {
                return TagContents(emptyList(), null, text, "Kein gültiges JSON: ${e.message}")
            }
            val obj = element as? JsonObject
                ?: return TagContents(emptyList(), null, text, "Das JSON ist kein Objekt.")
            val known = labels.toMap()
            val fields = labels.mapNotNull { (key, label) -> obj[key]?.let { display(it) }?.let { TagField(key, label, it) } } +
                obj.filterKeys { it !in known }.mapNotNull { (key, value) -> display(value)?.let { TagField(key, key, it) } }
            fun s(key: String) = obj[key]?.let(::display)
            val itemData = ItemJsonData(
                protocol = s("protocol"),
                version = s("version"),
                type = s("type"),
                color_hex = s("color_hex"),
                brand = s("brand"),
                min_temp = s("min_temp"),
                max_temp = s("max_temp"),
                bed_min_temp = s("bed_min_temp"),
                bed_max_temp = s("bed_max_temp"),
                variant = s("variant") ?: s("subtype"),
                color = s("color")
            )
            return TagContents(fields, itemData, text)
        }

        private fun display(element: JsonElement): String? = when (element) {
            is JsonNull -> null
            is JsonPrimitive -> element.contentOrNull?.takeIf { it.isNotBlank() }
            is JsonArray -> element.mapNotNull(::display).joinToString(", ").takeIf { it.isNotEmpty() }
            is JsonObject -> element.toString()
        }
    }
}
