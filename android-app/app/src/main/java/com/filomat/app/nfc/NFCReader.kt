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
import kotlinx.serialization.json.Json
import kotlinx.serialization.decodeFromString
import com.filomat.app.data.ItemJsonData

class NFCReader(private val activity: Activity) {
    // JSON-Instanz mit ignoreUnknownKeys = true für Item-Daten
    private val json = Json {
        ignoreUnknownKeys = true
    }
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
            _errorMessage.value = "NFC ist auf diesem Gerät nicht verfügbar oder nicht aktiviert."
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
                        try {
                            val itemData = json.decodeFromString<ItemJsonData>(jsonString)
                            _lastScannedItemData.value = itemData
                            // Rufe Callback mit Tag-ID und Item-Daten auf
                            onTagScanned?.invoke(tagId)
                        } catch (e: Exception) {
                            _errorMessage.value = "Fehler beim Parsen der JSON-Daten: ${e.message}"
                            // Rufe Callback ohne Item-Daten auf
                            onTagScanned?.invoke(tagId)
                        }
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
            _errorMessage.value = "Fehler beim Lesen des Tags: ${e.message}"
            // Fallback: Verwende Tag-ID
            val tagId = bytesToHex(tag.id)
            _lastScannedTag.value = tagId
            onTagScanned?.invoke(tagId)
        }
    }
    
    fun startScanningForItem(callback: (String, ItemJsonData?) -> Unit) {
        if (!isAvailable) {
            _errorMessage.value = "NFC ist auf diesem Gerät nicht verfügbar oder nicht aktiviert."
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
            _errorMessage.value = "Fehler beim Schreiben: ${e.message}"
            false
        }
    }
    
    fun writeJsonTag(jsonData: String, tag: Tag): Boolean {
        return try {
            val ndef = Ndef.get(tag)
            if (ndef != null) {
                ndef.connect()
                if (!ndef.isWritable) {
                    _errorMessage.value = "Tag ist nicht beschreibbar"
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
                    _errorMessage.value = "Tag zu klein: benötigt $messageSize Bytes, verfügbar: $maxSize Bytes"
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
                    _errorMessage.value = "Tag unterstützt NDEF nicht"
                    false
                }
            }
        } catch (e: Exception) {
            _errorMessage.value = "Fehler beim Schreiben: ${e.message ?: e.javaClass.simpleName}"
            false
        }
    }
    
    fun startWriting(callback: (Tag) -> Unit) {
        if (!isAvailable) {
            _errorMessage.value = "NFC ist auf diesem Gerät nicht verfügbar oder nicht aktiviert."
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
