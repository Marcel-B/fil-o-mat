package com.filomat.app.nfc

import android.app.Activity
import android.app.PendingIntent
import android.content.Intent
import android.content.IntentFilter
import android.nfc.NdefMessage
import android.nfc.NdefRecord
import android.nfc.NfcAdapter
import android.nfc.Tag
import android.nfc.tech.Ndef
import android.nfc.tech.NdefFormatable
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import com.filomat.app.data.ItemJsonData
import com.filomat.app.i18n.tr

class NFCReader(private val activity: Activity) {
    private val nfcAdapter: NfcAdapter? = NfcAdapter.getDefaultAdapter(activity)
    
    private val _isScanning = MutableStateFlow(false)
    val isScanning: StateFlow<Boolean> = _isScanning.asStateFlow()
    
    private val _lastScannedTag = MutableStateFlow<String?>(null)
    val lastScannedTag: StateFlow<String?> = _lastScannedTag.asStateFlow()
    
    private val _lastScannedItemData = MutableStateFlow<ItemJsonData?>(null)
    val lastScannedItemData: StateFlow<ItemJsonData?> = _lastScannedItemData.asStateFlow()
    
    private val _errorMessage = MutableStateFlow<String?>(null)
    val errorMessage: StateFlow<String?> = _errorMessage.asStateFlow()
    
    private var onTagScanned: ((String) -> Unit)? = null
    private var onItemDataScanned: ((ItemJsonData) -> Unit)? = null
    
    val isAvailable: Boolean
        get() = nfcAdapter != null && nfcAdapter.isEnabled
    
    fun startScanning(callback: (String) -> Unit) {
        if (!isAvailable) {
            _errorMessage.value = tr("NFC ist auf diesem Gerät nicht verfügbar oder nicht aktiviert.")
            return
        }
        
        onTagScanned = callback
        _isScanning.value = true
        _errorMessage.value = null
        
        enableReaderMode()
    }
    
    fun stopScanning() {
        disableReaderMode()
        _isScanning.value = false
        onTagScanned = null
    }
    
    fun handleIntent(intent: Intent) {
        if (NfcAdapter.ACTION_NDEF_DISCOVERED == intent.action ||
            NfcAdapter.ACTION_TAG_DISCOVERED == intent.action ||
            NfcAdapter.ACTION_TECH_DISCOVERED == intent.action) {
            
            val tag: Tag? = intent.getParcelableExtra(NfcAdapter.EXTRA_TAG)
            tag?.let { processTag(it) }
        }
    }
    
    private fun processTag(tag: Tag) {
        try {
            // Tag-ID immer zuerst setzen
            val tagId = bytesToHex(tag.id)
            _lastScannedTag.value = tagId
            
            val ndef = Ndef.get(tag)
            if (ndef != null) {
                ndef.connect()
                val ndefMessage = ndef.ndefMessage
                if (ndefMessage != null && ndefMessage.records.isNotEmpty()) {
                    val record = ndefMessage.records[0]
                    val payload = record.payload
                    
                    // Prüfe ob es ein MIME Media Record mit application/json ist
                    val tnf = record.tnf
                    val type = record.type
                    val typeString = String(type, Charsets.UTF_8)
                    
                    if (tnf == NdefRecord.TNF_MIME_MEDIA && typeString == "application/json") {
                        // JSON-Daten parsen
                        val jsonString = String(payload, Charsets.UTF_8)
                        // Nachsichtig lesen: andere OpenSpool-Writer speichern Temperaturen als Zahl
                        val contents = TagContents.parse(jsonString)
                        if (contents.error == null) {
                            _lastScannedItemData.value = contents.itemData
                        } else {
                            _errorMessage.value = tr("Fehler beim Parsen der JSON-Daten: %s", contents.error)
                        }
                        onTagScanned?.invoke(tagId)
                    } else {
                        // Normale Text-Daten
                        val tagString = String(payload, Charsets.UTF_8)
                        _lastScannedTag.value = tagString
                        onTagScanned?.invoke(tagString)
                    }
                } else {
                    // Fallback: Verwende Tag-ID
                    onTagScanned?.invoke(tagId)
                }
                ndef.close()
            } else {
                // Tag unterstützt NDEF nicht, verwende Tag-ID
                onTagScanned?.invoke(tagId)
            }
        } catch (e: Exception) {
            _errorMessage.value = tr("Fehler beim Lesen des Tags: %s", e.message)
            // Fallback: Verwende Tag-ID
            val tagId = bytesToHex(tag.id)
            _lastScannedTag.value = tagId
            onTagScanned?.invoke(tagId)
        }
    }
    
    fun startScanningForItem(callback: (String, ItemJsonData?) -> Unit) {
        if (!isAvailable) {
            _errorMessage.value = tr("NFC ist auf diesem Gerät nicht verfügbar oder nicht aktiviert.")
            return
        }
        
        // Setze Callbacks zurück und initialisiere State
        _lastScannedItemData.value = null
        _lastScannedTag.value = null
        onItemDataScanned = null
        onTagScanned = { tag ->
            val itemData = _lastScannedItemData.value
            callback(tag, itemData)
        }
        _isScanning.value = true
        _errorMessage.value = null
        
        enableReaderMode()
    }
    
    /**
     * Liest einen einzelnen Tag vollständig aus (UID, Chip, alle NDEF-Records, Filamentdaten), ohne etwas
     * in der App zu ändern. Der Callback läuft auf dem Main-Thread.
     */
    fun readTag(callback: (TagReading) -> Unit) {
        if (!isAvailable) {
            _errorMessage.value = tr("NFC ist auf diesem Gerät nicht verfügbar oder nicht aktiviert.")
            return
        }
        _isScanning.value = true
        _errorMessage.value = null
        nfcAdapter?.enableReaderMode(
            activity,
            { tag ->
                val reading = describeTag(tag)
                android.os.Handler(android.os.Looper.getMainLooper()).post {
                    disableReaderMode()
                    _isScanning.value = false
                    callback(reading)
                }
            },
            NfcAdapter.FLAG_READER_NFC_A or
                    NfcAdapter.FLAG_READER_NFC_B or
                    NfcAdapter.FLAG_READER_NFC_F or
                    NfcAdapter.FLAG_READER_NFC_V,
            null
        )
    }

    /**
     * Scannt einen Tag für die Abläufe im Scannen-Tab: die Kennung (wie beim bisherigen Scannen die UID, bei einem
     * reinen Text-Tag dessen Inhalt) und die Filamentdaten. Der Callback läuft auf dem Main-Thread.
     */
    fun scanTag(callback: (ScannedTag) -> Unit) {
        if (!isAvailable) {
            _errorMessage.value = tr("NFC ist auf diesem Gerät nicht verfügbar oder nicht aktiviert.")
            return
        }
        _isScanning.value = true
        _errorMessage.value = null
        nfcAdapter?.enableReaderMode(
            activity,
            { tag ->
                val scanned = scannedTag(tag)
                android.os.Handler(android.os.Looper.getMainLooper()).post {
                    disableReaderMode()
                    _isScanning.value = false
                    callback(scanned)
                }
            },
            NfcAdapter.FLAG_READER_NFC_A or
                    NfcAdapter.FLAG_READER_NFC_B or
                    NfcAdapter.FLAG_READER_NFC_F or
                    NfcAdapter.FLAG_READER_NFC_V,
            null
        )
    }

    /** Dieselbe Kennung wie `processTag`, damit vorhandene Container und Filamente gefunden werden. */
    private fun scannedTag(tag: Tag): ScannedTag {
        val uid = bytesToHex(tag.id)
        val ndef = Ndef.get(tag) ?: return ScannedTag(uid)
        return try {
            ndef.connect()
            val record = ndef.ndefMessage?.records?.firstOrNull() ?: return ScannedTag(uid)
            val payload = record.payload ?: ByteArray(0)
            if (record.tnf == NdefRecord.TNF_MIME_MEDIA && String(record.type, Charsets.UTF_8) == "application/json") {
                val contents = TagContents.parse(String(payload, Charsets.UTF_8))
                ScannedTag(uid, contents.itemData, contents.error?.let { tr("Filamentdaten auf dem Tag nicht lesbar: %s", it) })
            } else {
                ScannedTag(String(payload, Charsets.UTF_8))
            }
        } catch (e: Exception) {
            ScannedTag(uid, parseError = tr("Fehler beim Lesen des Tags: %s", e.message ?: e.javaClass.simpleName))
        } finally {
            try { ndef.close() } catch (_: Exception) {}
        }
    }

    private fun describeTag(tag: Tag): TagReading {
        val base = TagReading(
            uid = bytesToHex(tag.id),
            technologies = tag.techList.map { it.substringAfterLast('.') }
        )
        val ndef = Ndef.get(tag) ?: return base.copy(
            error = if (NdefFormatable.get(tag) != null) tr("Der Tag ist leer (noch nicht für NDEF formatiert).") else null
        )
        return try {
            ndef.connect()
            val message = ndef.ndefMessage
            val records = message?.records?.map(::describeRecord) ?: emptyList()
            val json = records.firstOrNull { it.type == "application/json" }?.text
            // Wie beim Scannen: steht vorne kein JSON, ist der Inhalt des ersten Records die Kennung
            val first = message?.records?.firstOrNull()
            val textId = first?.takeIf { !(it.tnf == NdefRecord.TNF_MIME_MEDIA && String(it.type, Charsets.UTF_8) == "application/json") }
                ?.let { String(it.payload ?: ByteArray(0), Charsets.UTF_8) }
            base.copy(
                ndefType = ndef.type,
                ndefCapacity = ndef.maxSize,
                ndefUsed = message?.byteArrayLength,
                isWritable = ndef.isWritable,
                records = records,
                contents = json?.let(TagContents::parse),
                textId = textId
            )
        } catch (e: Exception) {
            base.copy(error = tr("Fehler beim Lesen des Tags: %s", e.message ?: e.javaClass.simpleName))
        } finally {
            try { ndef.close() } catch (_: Exception) {}
        }
    }

    private fun describeRecord(record: NdefRecord): TagRecord {
        val type = String(record.type, Charsets.UTF_8)
        val payload = record.payload ?: ByteArray(0)
        val format = when (record.tnf) {
            NdefRecord.TNF_MIME_MEDIA -> "MIME"
            NdefRecord.TNF_WELL_KNOWN -> "Well-known"
            NdefRecord.TNF_ABSOLUTE_URI -> "URI"
            NdefRecord.TNF_EXTERNAL_TYPE -> tr("Extern")
            NdefRecord.TNF_EMPTY -> tr("Leer")
            else -> tr("Unbekannt")
        }
        val text = when {
            record.tnf == NdefRecord.TNF_WELL_KNOWN && type == "T" && payload.isNotEmpty() -> {
                // Text-Record: Statusbyte, Sprachcode, dann der Text
                val languageLength = payload[0].toInt() and 0x3F
                val utf16 = payload[0].toInt() and 0x80 != 0
                String(payload, 1 + languageLength, payload.size - 1 - languageLength, if (utf16) Charsets.UTF_16 else Charsets.UTF_8)
            }
            record.tnf == NdefRecord.TNF_WELL_KNOWN && type == "U" -> record.toUri()?.toString()
            else -> String(payload, Charsets.UTF_8).takeIf { text -> text.all { !it.isISOControl() || it == '\n' || it == '\r' || it == '\t' } }
        }
        return TagRecord(format, type, payload.size, text)
    }

    fun writeTag(message: String, tag: Tag): Boolean {
        return try {
            val ndef = Ndef.get(tag)
            if (ndef != null) {
                ndef.connect()
                if (!ndef.isWritable) {
                    return false
                }
                
                val record = NdefRecord.createTextRecord("en", message)
                val ndefMessage = NdefMessage(arrayOf(record))
                
                if (ndef.maxSize < ndefMessage.byteArrayLength) {
                    return false
                }
                
                ndef.writeNdefMessage(ndefMessage)
                ndef.close()
                true
            } else {
                // Versuche Formatable
                val format = NdefFormatable.get(tag)
                if (format != null) {
                    format.connect()
                    val record = NdefRecord.createTextRecord("en", message)
                    val ndefMessage = NdefMessage(arrayOf(record))
                    format.format(ndefMessage)
                    format.close()
                    true
                } else {
                    false
                }
            }
        } catch (e: Exception) {
            _errorMessage.value = tr("Fehler beim Schreiben: %s", e.message)
            false
        }
    }
    
    fun writeJsonTag(jsonData: String, tag: Tag): Boolean {
        return try {
            val ndef = Ndef.get(tag)
            if (ndef != null) {
                ndef.connect()
                if (!ndef.isWritable) {
                    _errorMessage.value = tr("Tag ist nicht beschreibbar")
                    ndef.close()
                    return false
                }
                
                // Erstelle NDEF Record mit MIME-Type application/json
                val mimeType = "application/json".toByteArray(Charsets.UTF_8)
                val payload = jsonData.toByteArray(Charsets.UTF_8)
                val record = NdefRecord(NdefRecord.TNF_MIME_MEDIA, mimeType, ByteArray(0), payload)
                val ndefMessage = NdefMessage(arrayOf(record))
                
                val messageSize = ndefMessage.byteArrayLength
                val maxSize = ndef.maxSize
                
                if (maxSize < messageSize) {
                    _errorMessage.value = tr("Tag zu klein: benötigt %s Bytes, verfügbar: %s Bytes", messageSize, maxSize)
                    ndef.close()
                    return false
                }
                
                ndef.writeNdefMessage(ndefMessage)
                ndef.close()
                _errorMessage.value = null
                true
            } else {
                // Versuche Formatable
                val format = NdefFormatable.get(tag)
                if (format != null) {
                    format.connect()
                    val mimeType = "application/json".toByteArray(Charsets.UTF_8)
                    val payload = jsonData.toByteArray(Charsets.UTF_8)
                    val record = NdefRecord(NdefRecord.TNF_MIME_MEDIA, mimeType, ByteArray(0), payload)
                    val ndefMessage = NdefMessage(arrayOf(record))
                    format.format(ndefMessage)
                    format.close()
                    _errorMessage.value = null
                    true
                } else {
                    _errorMessage.value = tr("Tag unterstützt NDEF nicht")
                    false
                }
            }
        } catch (e: Exception) {
            _errorMessage.value = tr("Fehler beim Schreiben: %s", e.message ?: e.javaClass.simpleName)
            false
        }
    }
    
    fun startWriting(callback: (Tag) -> Unit) {
        if (!isAvailable) {
            _errorMessage.value = tr("NFC ist auf diesem Gerät nicht verfügbar oder nicht aktiviert.")
            return
        }
        
        _isScanning.value = true
        _errorMessage.value = null
        
        enableWriterMode(callback)
    }
    
    private var onTagForWriting: ((Tag) -> Unit)? = null
    
    private fun enableWriterMode(callback: (Tag) -> Unit) {
        onTagForWriting = callback
        nfcAdapter?.enableReaderMode(
            activity,
            { tag -> 
                onTagForWriting?.invoke(tag)
                onTagForWriting = null
                // Warte kurz, bevor der Reader-Mode deaktiviert wird
                // damit der Callback die Daten schreiben kann
                android.os.Handler(android.os.Looper.getMainLooper()).postDelayed({
                    disableReaderMode()
                    _isScanning.value = false
                }, 100)
            },
            NfcAdapter.FLAG_READER_NFC_A or
                    NfcAdapter.FLAG_READER_NFC_B or
                    NfcAdapter.FLAG_READER_NFC_F or
                    NfcAdapter.FLAG_READER_NFC_V,
            null
        )
    }
    
    private fun enableReaderMode() {
        nfcAdapter?.enableReaderMode(
            activity,
            { tag -> processTag(tag) },
            NfcAdapter.FLAG_READER_NFC_A or
                    NfcAdapter.FLAG_READER_NFC_B or
                    NfcAdapter.FLAG_READER_NFC_F or
                    NfcAdapter.FLAG_READER_NFC_V,
            null
        )
    }
    
    private fun disableReaderMode() {
        nfcAdapter?.disableReaderMode(activity)
    }
    
    private fun bytesToHex(bytes: ByteArray): String {
        return bytes.joinToString("") { "%02X".format(it) }
    }
}
