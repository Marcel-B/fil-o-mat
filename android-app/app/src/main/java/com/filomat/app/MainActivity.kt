package com.filomat.app

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.ui.Modifier
import com.filomat.app.nfc.NFCReader
import com.filomat.app.ui.theme.FilomatTheme
import com.filomat.app.ui.navigation.AppNavigation

class MainActivity : ComponentActivity() {
    private lateinit var nfcReader: NFCReader
    
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        nfcReader = NFCReader(this)
        
        setContent {
            FilomatTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    AppNavigation()
                }
            }
        }
    }
    
    override fun onResume() {
        super.onResume()
        // NFC Reader Mode wird automatisch aktiviert wenn startScanning aufgerufen wird
    }
    
    override fun onPause() {
        super.onPause()
        nfcReader.stopScanning()
    }
    
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        nfcReader.handleIntent(intent)
    }
}
