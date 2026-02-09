package com.filomat.app.ui.navigation

import androidx.compose.runtime.Composable
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.filomat.app.ui.screens.ContainerDetailScreen
import com.filomat.app.ui.screens.ContainerListScreen
import com.filomat.app.ui.screens.ItemsOverviewScreen
import com.filomat.app.ui.screens.ScanScreen

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
        composable("items") {
            ItemsOverviewScreen(navController = navController)
        }
    }
}
