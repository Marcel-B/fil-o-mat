package com.filomat.app.ui.navigation

import androidx.compose.runtime.Composable
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.filomat.app.data.PlaceTarget
import com.filomat.app.ui.screens.BackendScreen
import com.filomat.app.ui.screens.ContainerScanScreen
import com.filomat.app.ui.screens.PlaceFlowScreen
import com.filomat.app.ui.screens.StockScreen
import com.filomat.app.ui.screens.TransmissionScreen
import com.filomat.app.ui.screens.ContainerDetailScreen
import com.filomat.app.ui.screens.ContainerListScreen
import com.filomat.app.ui.screens.ItemDetailScreen
import com.filomat.app.ui.screens.ItemsOverviewScreen
import com.filomat.app.ui.screens.PrintersScreen
import com.filomat.app.ui.screens.ScanScreen
import com.filomat.app.ui.screens.TagReadScreen

@Composable
fun AppNavigation(navController: NavHostController = rememberNavController()) {
    NavHost(
        navController = navController,
        startDestination = "containers"
    ) {
        composable("containers") {
            ContainerListScreen(navController = navController)
        }
        composable("container/{containerId}") { backStackEntry ->
            val containerId = backStackEntry.arguments?.getString("containerId") ?: ""
            ContainerDetailScreen(
                containerId = containerId,
                navController = navController
            )
        }
        composable("scan") {
            ScanScreen(navController = navController)
        }
        composable("read") {
            TagReadScreen(navController = navController)
        }
        composable("scan/container") {
            ContainerScanScreen(navController = navController)
        }
        composable("scan/place/{target}") { backStackEntry ->
            val target = if (backStackEntry.arguments?.getString("target") == "printer") PlaceTarget.PRINTER else PlaceTarget.CONTAINER
            PlaceFlowScreen(target = target, navController = navController)
        }
        composable("scan/transmission") {
            TransmissionScreen(navController = navController)
        }
        composable("stock") {
            StockScreen(navController = navController)
        }
        composable("items") {
            ItemsOverviewScreen(navController = navController)
        }
        composable("item/{itemId}") { backStackEntry ->
            ItemDetailScreen(
                itemId = backStackEntry.arguments?.getString("itemId") ?: "",
                navController = navController
            )
        }
        composable("printers") {
            PrintersScreen(navController = navController)
        }
        composable("backend") {
            BackendScreen(navController = navController)
        }
    }
}
