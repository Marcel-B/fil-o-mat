package com.filomat.app.data

import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import android.database.Cursor
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.decodeFromString
import java.io.InputStream

data class FilamentPreset(
    val type: String,
    val brand: String?,
    val minTemp: String,
    val maxTemp: String,
    val bedMinTemp: String?,
    val bedMaxTemp: String?
)

@Serializable
data class PresetJsonData(
    val type: String,
    val brand: String? = null,
    val min_temp: String,
    val max_temp: String,
    val bed_min_temp: String? = null,
    val bed_max_temp: String? = null
)

class FilamentPresetDatabase(context: Context) : SQLiteOpenHelper(context, DATABASE_NAME, null, DATABASE_VERSION) {
    
    private val appContext = context.applicationContext
    
    init {
        // Synchronisiere beim ersten Zugriff, falls Datenbank bereits existiert
        syncFromJsonIfNeeded()
    }
    
    companion object {
        private const val DATABASE_NAME = "filament_presets.db"
        private const val DATABASE_VERSION = 2
        private const val TABLE_PRESETS = "presets"
        private const val TABLE_BRANDS = "brands"
        private const val TABLE_VARIANTS = "variants"
        private const val COL_TYPE = "type"
        private const val COL_BRAND = "brand"
        private const val COL_MIN_TEMP = "min_temp"
        private const val COL_MAX_TEMP = "max_temp"
        private const val COL_BED_MIN_TEMP = "bed_min_temp"
        private const val COL_BED_MAX_TEMP = "bed_max_temp"
        private const val COL_NAME = "name"
    }
    
    override fun onCreate(db: SQLiteDatabase) {
        // Presets Tabelle
        val createPresetsTable = """
            CREATE TABLE $TABLE_PRESETS (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                $COL_TYPE TEXT NOT NULL,
                $COL_BRAND TEXT,
                $COL_MIN_TEMP TEXT NOT NULL,
                $COL_MAX_TEMP TEXT NOT NULL,
                $COL_BED_MIN_TEMP TEXT,
                $COL_BED_MAX_TEMP TEXT,
                UNIQUE($COL_TYPE, $COL_BRAND)
            )
        """.trimIndent()
        db.execSQL(createPresetsTable)
        
        // Brands Tabelle
        val createBrandsTable = """
            CREATE TABLE $TABLE_BRANDS (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                $COL_NAME TEXT UNIQUE NOT NULL
            )
        """.trimIndent()
        db.execSQL(createBrandsTable)
        
        // Variants Tabelle
        val createVariantsTable = """
            CREATE TABLE $TABLE_VARIANTS (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                $COL_NAME TEXT UNIQUE NOT NULL
            )
        """.trimIndent()
        db.execSQL(createVariantsTable)
        
        // Initiale Daten aus JSON einfügen
        insertInitialDataFromJson(db)
    }
    
    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
        if (oldVersion < 2) {
            // Erstelle neue Tabellen für Brands und Variants
            val createBrandsTable = """
                CREATE TABLE IF NOT EXISTS $TABLE_BRANDS (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    $COL_NAME TEXT UNIQUE NOT NULL
                )
            """.trimIndent()
            db.execSQL(createBrandsTable)
            
            val createVariantsTable = """
                CREATE TABLE IF NOT EXISTS $TABLE_VARIANTS (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    $COL_NAME TEXT UNIQUE NOT NULL
                )
            """.trimIndent()
            db.execSQL(createVariantsTable)
            
            // Lade Initialdaten für Brands und Variants aus JSON
            insertBrandsAndVariantsFromJson(db)
        }
    }
    
    private fun insertInitialDataFromJson(db: SQLiteDatabase) {
        try {
            // Lade Presets aus JSON
            val presetsJson = appContext.assets.open("presets.json").bufferedReader().use { it.readText() }
            val presets = Json.decodeFromString<List<PresetJsonData>>(presetsJson)
            presets.forEach { preset ->
                insertPreset(db, preset.type, preset.brand, preset.min_temp, preset.max_temp, preset.bed_min_temp, preset.bed_max_temp)
            }
            android.util.Log.d("FilamentPresetDB", "Presets erfolgreich aus JSON geladen: ${presets.size} Einträge")
        } catch (e: Exception) {
            // Fallback: Verwende hardcodierte Daten
            android.util.Log.e("FilamentPresetDB", "Fehler beim Laden der Presets aus JSON: ${e.message}, verwende Fallback")
            insertInitialDataFallback(db)
        }
        
        // Lade Brands und Variants aus JSON
        insertBrandsAndVariantsFromJson(db)
    }
    
    private fun insertInitialDataFallback(db: SQLiteDatabase) {
        // Fallback: Minimal hardcodierte Presets
        insertPreset(db, "PLA", null, "190", "220", "50", "60")
        insertPreset(db, "PETG", null, "220", "250", "70", "80")
        insertPreset(db, "TPU", null, "220", "250", "40", "60")
        insertPreset(db, "ABS", null, "230", "260", "80", "100")
        insertPreset(db, "ASA", null, "240", "270", "80", "100")
    }
    
    private fun insertBrandsAndVariantsFromJson(db: SQLiteDatabase) {
        try {
            // Lade Brands aus JSON
            val brandsJson = appContext.assets.open("brands.json").bufferedReader().use { it.readText() }
            val brands = Json.decodeFromString<List<String>>(brandsJson)
            brands.forEach { brand ->
                val values = android.content.ContentValues().apply {
                    put(COL_NAME, brand)
                }
                db.insertWithOnConflict(TABLE_BRANDS, null, values, SQLiteDatabase.CONFLICT_IGNORE)
            }
            android.util.Log.d("FilamentPresetDB", "Brands erfolgreich aus JSON geladen: ${brands.size} Einträge")
        } catch (e: Exception) {
            android.util.Log.e("FilamentPresetDB", "Fehler beim Laden der Brands aus JSON: ${e.message}")
        }
        
        try {
            // Lade Variants aus JSON
            val variantsJson = appContext.assets.open("variants.json").bufferedReader().use { it.readText() }
            val variants = Json.decodeFromString<List<String>>(variantsJson)
            variants.forEach { variant ->
                val values = android.content.ContentValues().apply {
                    put(COL_NAME, variant)
                }
                db.insertWithOnConflict(TABLE_VARIANTS, null, values, SQLiteDatabase.CONFLICT_IGNORE)
            }
            android.util.Log.d("FilamentPresetDB", "Variants erfolgreich aus JSON geladen: ${variants.size} Einträge")
        } catch (e: Exception) {
            android.util.Log.e("FilamentPresetDB", "Fehler beim Laden der Variants aus JSON: ${e.message}")
        }
    }
    
    private fun insertPreset(
        db: SQLiteDatabase,
        type: String,
        brand: String?,
        minTemp: String,
        maxTemp: String,
        bedMinTemp: String?,
        bedMaxTemp: String?
    ) {
        val values = android.content.ContentValues().apply {
            put(COL_TYPE, type)
            put(COL_BRAND, brand)
            put(COL_MIN_TEMP, minTemp)
            put(COL_MAX_TEMP, maxTemp)
            put(COL_BED_MIN_TEMP, bedMinTemp)
            put(COL_BED_MAX_TEMP, bedMaxTemp)
        }
        db.insertWithOnConflict(TABLE_PRESETS, null, values, SQLiteDatabase.CONFLICT_REPLACE)
    }
    
    /**
     * Synchronisiert die Datenbank mit den JSON-Dateien.
     * Wird beim ersten Zugriff aufgerufen, wenn die Datenbank bereits existiert.
     */
    private fun syncFromJsonIfNeeded() {
        try {
            val dbFile = appContext.getDatabasePath(DATABASE_NAME)
            if (dbFile.exists()) {
                // Datenbank existiert bereits, synchronisiere mit JSON
                val db = writableDatabase
                syncPresetsFromJson(db)
                syncBrandsAndVariantsFromJson(db)
                db.close()
            }
        } catch (e: Exception) {
            android.util.Log.e("FilamentPresetDB", "Fehler bei Synchronisation: ${e.message}")
        }
    }
    
    /**
     * Synchronisiert Presets aus JSON mit der Datenbank (Upsert).
     */
    private fun syncPresetsFromJson(db: SQLiteDatabase) {
        try {
            val presetsJson = appContext.assets.open("presets.json").bufferedReader().use { it.readText() }
            val presets = Json.decodeFromString<List<PresetJsonData>>(presetsJson)
            presets.forEach { preset ->
                insertPreset(db, preset.type, preset.brand, preset.min_temp, preset.max_temp, preset.bed_min_temp, preset.bed_max_temp)
            }
            android.util.Log.d("FilamentPresetDB", "Presets synchronisiert: ${presets.size} Einträge")
        } catch (e: Exception) {
            android.util.Log.e("FilamentPresetDB", "Fehler beim Synchronisieren der Presets: ${e.message}")
        }
    }
    
    /**
     * Synchronisiert Brands und Variants aus JSON mit der Datenbank (Upsert).
     */
    private fun syncBrandsAndVariantsFromJson(db: SQLiteDatabase) {
        try {
            // Lade Brands aus JSON
            val brandsJson = appContext.assets.open("brands.json").bufferedReader().use { it.readText() }
            val brands = Json.decodeFromString<List<String>>(brandsJson)
            brands.forEach { brand ->
                val values = android.content.ContentValues().apply {
                    put(COL_NAME, brand)
                }
                db.insertWithOnConflict(TABLE_BRANDS, null, values, SQLiteDatabase.CONFLICT_REPLACE)
            }
            android.util.Log.d("FilamentPresetDB", "Brands synchronisiert: ${brands.size} Einträge")
        } catch (e: Exception) {
            android.util.Log.e("FilamentPresetDB", "Fehler beim Synchronisieren der Brands: ${e.message}")
        }
        
        try {
            // Lade Variants aus JSON
            val variantsJson = appContext.assets.open("variants.json").bufferedReader().use { it.readText() }
            val variants = Json.decodeFromString<List<String>>(variantsJson)
            variants.forEach { variant ->
                val values = android.content.ContentValues().apply {
                    put(COL_NAME, variant)
                }
                db.insertWithOnConflict(TABLE_VARIANTS, null, values, SQLiteDatabase.CONFLICT_REPLACE)
            }
            android.util.Log.d("FilamentPresetDB", "Variants synchronisiert: ${variants.size} Einträge")
        } catch (e: Exception) {
            android.util.Log.e("FilamentPresetDB", "Fehler beim Synchronisieren der Variants: ${e.message}")
        }
    }
    
    fun getPreset(type: String, brand: String?): FilamentPreset? {
        val db = readableDatabase
        val cursor: Cursor = if (brand != null && brand.isNotEmpty()) {
            // Suche zuerst nach spezifischer Marke
            db.query(
                TABLE_PRESETS,
                null,
                "$COL_TYPE = ? AND $COL_BRAND = ?",
                arrayOf(type, brand),
                null,
                null,
                null
            )
        } else {
            // Fallback: Suche nach Standard (brand = null)
            db.query(
                TABLE_PRESETS,
                null,
                "$COL_TYPE = ? AND $COL_BRAND IS NULL",
                arrayOf(type),
                null,
                null,
                null
            )
        }
        
        return if (cursor.moveToFirst()) {
            val preset = FilamentPreset(
                type = cursor.getString(cursor.getColumnIndexOrThrow(COL_TYPE)),
                brand = cursor.getStringOrNull(cursor.getColumnIndexOrThrow(COL_BRAND)),
                minTemp = cursor.getString(cursor.getColumnIndexOrThrow(COL_MIN_TEMP)),
                maxTemp = cursor.getString(cursor.getColumnIndexOrThrow(COL_MAX_TEMP)),
                bedMinTemp = cursor.getStringOrNull(cursor.getColumnIndexOrThrow(COL_BED_MIN_TEMP)),
                bedMaxTemp = cursor.getStringOrNull(cursor.getColumnIndexOrThrow(COL_BED_MAX_TEMP))
            )
            cursor.close()
            preset
        } else {
            cursor.close()
            // Fallback: Wenn keine spezifische Marke gefunden, versuche Standard
            if (brand != null && brand.isNotEmpty()) {
                getPreset(type, null)
            } else {
                null
            }
        }
    }
    
    fun getAllBrands(): List<String> {
        val db = readableDatabase
        val cursor = db.query(
            TABLE_BRANDS,
            arrayOf(COL_NAME),
            null,
            null,
            null,
            null,
            "$COL_NAME ASC"
        )
        val brands = mutableListOf<String>()
        while (cursor.moveToNext()) {
            brands.add(cursor.getString(cursor.getColumnIndexOrThrow(COL_NAME)))
        }
        cursor.close()
        return brands
    }
    
    fun getAllVariants(): List<String> {
        val db = readableDatabase
        val cursor = db.query(
            TABLE_VARIANTS,
            arrayOf(COL_NAME),
            null,
            null,
            null,
            null,
            "$COL_NAME ASC"
        )
        val variants = mutableListOf<String>()
        while (cursor.moveToNext()) {
            variants.add(cursor.getString(cursor.getColumnIndexOrThrow(COL_NAME)))
        }
        cursor.close()
        return variants
    }
    
    private fun Cursor.getStringOrNull(columnIndex: Int): String? {
        return if (isNull(columnIndex)) null else getString(columnIndex)
    }
}
