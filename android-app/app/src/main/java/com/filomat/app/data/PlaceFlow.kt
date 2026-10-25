package com.filomat.app.data

import com.filomat.app.data.sync.Spool
import com.filomat.app.data.sync.SpoolPlace
import com.filomat.app.data.sync.StoreState

/** Wohin ein Filament gebracht wird. */
enum class PlaceTarget {
    /** In einen Container im Lager */
    CONTAINER,
    /** In einen Kopf eines Druckers */
    PRINTER
}

/** Was von einer Sorte bewegt wird. */
sealed class SpoolChoice {
    /** Die Sorte selbst: Musterplättchen mit den vollen Spulen im Vorrat. */
    object Stock : SpoolChoice()
    /** Eine angebrochene Spule. */
    data class Opened(val spoolId: String) : SpoolChoice()
    /** Eine volle Spule aus dem Vorrat anbrechen. */
    object NewSpool : SpoolChoice()
}

enum class PlaceStep { SCAN_FILAMENT, CHOOSE_SPOOL, CHOOSE_TARGET }

/** Was am Ende von "Einlagern" bzw. "In Drucker einlegen" passiert. */
sealed class PlaceAction {
    data class MoveItem(val item: Item, val container: Container) : PlaceAction()
    data class MoveSpool(val item: Item, val spool: Spool, val place: SpoolPlace) : PlaceAction()
    data class OpenSpool(val item: Item, val place: SpoolPlace) : PlaceAction()
}

/**
 * Ablauf "Einlagern" bzw. "In Drucker einlegen" (wie `PlaceFlowModel` der iOS-App): erst das Filament
 * (Musterplättchen) scannen, dann wählen, welche Spule, dann den Container scannen bzw. Drucker und Kopf wählen.
 * Unveränderlich; jeder Schritt liefert den nächsten Stand.
 */
data class PlaceFlow(
    val target: PlaceTarget,
    val step: PlaceStep = PlaceStep.SCAN_FILAMENT,
    val item: Item? = null,
    val choice: SpoolChoice? = null,
    val containerId: String? = null,
    val printerPlace: SpoolPlace? = null,
    val error: String? = null,
    /** Gescanntes Plättchen, das die App noch nicht kennt; wartet auf „neu anlegen“ oder „zu vorhandenem hinzufügen“. */
    val unknownTag: String? = null,
    val unknownData: ItemJsonData? = null,
    /** Ergebnis des letzten Vorgangs; bleibt stehen, bis das nächste Filament gescannt wird. */
    val done: String? = null
) {
    val title: String get() = if (target == PlaceTarget.CONTAINER) "Einlagern" else "In Drucker einlegen"

    val actionTitle: String
        get() = when {
            target == PlaceTarget.CONTAINER -> if (choice == SpoolChoice.NewSpool) "Anbrechen und einlagern" else "Einlagern"
            choice == SpoolChoice.NewSpool -> "Anbrechen und einlegen"
            else -> "Einlegen"
        }

    /** Was von der gescannten Sorte bewegt werden kann. */
    fun choices(state: StoreState, connected: Boolean): List<SpoolChoice> {
        val item = item ?: return emptyList()
        val opened = state.spoolsOf(item.id).map { SpoolChoice.Opened(it.id) }
        return when (target) {
            // Anbrechen geht nur über das Backend, das die Spulen pflegt
            PlaceTarget.CONTAINER -> listOf(SpoolChoice.Stock) + opened + if (connected) listOf(SpoolChoice.NewSpool) else emptyList()
            // Spulen pflegt nur das Backend
            PlaceTarget.PRINTER -> if (connected) opened + SpoolChoice.NewSpool else emptyList()
        }
    }

    // Schritte

    /**
     * Filament-Tag gescannt. Ist das Plättchen noch unbekannt, wird nichts angelegt: der Ablauf wartet auf
     * [withNewItem] oder [withItem] (nachdem der Aufrufer das Tag dem gewählten Filament hinzugefügt hat).
     */
    fun withFilament(tag: String, itemData: ItemJsonData?, state: StoreState, connected: Boolean): PlaceFlow {
        val tagId = tag.trim()
        if (tagId.isEmpty()) return copy(error = "Ungültiger Tag.")
        state.findContainerByTag(tagId)?.let {
            return copy(error = "Das ist der Container „${it.name}“. Scanne zuerst das Filament.")
        }
        val existing = state.findItemByTag(tagId)
            ?: return PlaceFlow(target, unknownTag = tagId, unknownData = itemData)
        return withItem(existing, state, connected)
    }

    /** Das unbekannte Plättchen wird ein neues Filament; die zweite Hälfte muss der Aufrufer speichern. */
    fun withNewItem(state: StoreState, connected: Boolean): Pair<PlaceFlow, Item?> {
        val tagId = unknownTag ?: return this to null
        val item = Item(rfidTag = tagId, name = defaultName(unknownData), itemData = unknownData)
        return withItem(item, state, connected) to item
    }

    /** Ablauf mit dieser Sorte fortsetzen. */
    fun withItem(item: Item, state: StoreState, connected: Boolean): PlaceFlow {
        val started = PlaceFlow(target, item = item)
        val options = started.choices(state, connected)
        return if (options.size == 1) started.choose(options[0], state) else started.copy(step = PlaceStep.CHOOSE_SPOOL)
    }

    fun choose(choice: SpoolChoice, state: StoreState): PlaceFlow {
        var next = copy(choice = choice, step = PlaceStep.CHOOSE_TARGET)
        // Steckt die Spule schon in einem Drucker, ist das der naheliegende Drucker
        if (target == PlaceTarget.PRINTER && printerPlace == null && choice is SpoolChoice.Opened) {
            val spool = state.spool(choice.spoolId)
            if (spool?.printerId != null) next = next.copy(printerPlace = spool.place)
        }
        return next
    }

    /** Container-Tag im Schritt "Ziel". Im Druckerablauf wählt der Container eines Druckers diesen Drucker. */
    fun withTargetTag(tag: String, state: StoreState): PlaceFlow {
        val tagId = tag.trim()
        val found = state.findContainerByTag(tagId) ?: return copy(
            error = if (state.findItemByTag(tagId) != null) "Das ist ein Filament-Tag. Scanne jetzt den Container."
            else "Container nicht gefunden. Lege ihn zuerst unter „Container“ an."
        )
        return when (target) {
            PlaceTarget.CONTAINER -> selectContainer(found, state)
            PlaceTarget.PRINTER -> state.printerOwning(found.id)?.let { selectPlace(SpoolPlace.printer(it.id, 0)) }
                ?: copy(error = "„${found.name}“ ist kein Drucker. Wähle Drucker und Kopf aus der Liste.")
        }
    }

    fun selectContainer(container: Container, state: StoreState): PlaceFlow {
        state.printerOwning(container.id)?.let {
            return copy(error = "Das ist der Container von „${it.name}“. Nimm dafür „In Drucker einlegen“.")
        }
        return copy(containerId = container.id, error = null)
    }

    fun selectPlace(place: SpoolPlace): PlaceFlow = copy(printerPlace = place, error = null)

    fun failed(message: String): PlaceFlow = copy(error = message)

    /** Zurück zum ersten Schritt; ein Ergebnis bleibt stehen. */
    fun reset(done: String? = this.done): PlaceFlow = PlaceFlow(target, done = done)

    // Zustand

    /** Die gescannte Sorte im aktuellen Stand. */
    fun currentItem(state: StoreState): Item? = item?.let { state.item(it.id) ?: it }

    /** Wo das Gewählte gerade ist, z. B. "U1 Kopf 2" oder "Regal oben". */
    fun currentPlaceText(state: StoreState): String? = when (val choice = choice) {
        SpoolChoice.Stock -> currentItem(state)?.containerId?.let { state.container(it)?.name } ?: "kein Container"
        is SpoolChoice.Opened -> state.spool(choice.spoolId)?.locationText(state.printers, state.containers)
        else -> null
    }

    /** Das Gewählte ist schon am Ziel. */
    fun isAlreadyThere(state: StoreState): Boolean {
        val choice = choice ?: return false
        return when {
            target == PlaceTarget.CONTAINER && choice == SpoolChoice.Stock ->
                containerId != null && currentItem(state)?.containerId == containerId
            target == PlaceTarget.CONTAINER && choice is SpoolChoice.Opened ->
                containerId != null && state.spool(choice.spoolId)?.place == SpoolPlace.container(containerId)
            target == PlaceTarget.PRINTER && choice is SpoolChoice.Opened ->
                printerPlace != null && state.spool(choice.spoolId)?.place == printerPlace
            else -> false
        }
    }

    /** Die Spule, die am gewählten Kopf steckt und dafür ins Lager wandert. */
    fun displacedSpool(state: StoreState): Spool? {
        if (target != PlaceTarget.PRINTER) return null
        val printerId = printerPlace?.printerId ?: return null
        val current = state.spoolIn(printerId, printerPlace.tool ?: 0) ?: return null
        val choice = choice
        if (choice is SpoolChoice.Opened && choice.spoolId == current.id) return null
        return current
    }

    fun canPerform(state: StoreState): Boolean {
        if (step != PlaceStep.CHOOSE_TARGET || choice == null || isAlreadyThere(state)) return false
        return when (target) {
            PlaceTarget.CONTAINER -> containerId != null
            PlaceTarget.PRINTER -> printerPlace?.printerId != null
        }
    }

    /** Was "Einlagern" bzw. "Einlegen" jetzt tun würde; `null`, solange etwas fehlt. */
    fun action(state: StoreState): PlaceAction? {
        if (!canPerform(state)) return null
        val item = currentItem(state) ?: return null
        val choice = choice
        return when (target) {
            PlaceTarget.CONTAINER -> {
                val container = containerId?.let { state.container(it) } ?: return null
                when (choice) {
                    SpoolChoice.Stock -> PlaceAction.MoveItem(item, container)
                    is SpoolChoice.Opened -> state.spool(choice.spoolId)?.let { PlaceAction.MoveSpool(item, it, SpoolPlace.container(container.id)) }
                    SpoolChoice.NewSpool -> PlaceAction.OpenSpool(item, SpoolPlace.container(container.id))
                    else -> null
                }
            }
            PlaceTarget.PRINTER -> {
                val place = printerPlace ?: return null
                when (choice) {
                    is SpoolChoice.Opened -> state.spool(choice.spoolId)?.let { PlaceAction.MoveSpool(item, it, place) }
                    SpoolChoice.NewSpool -> PlaceAction.OpenSpool(item, place)
                    else -> null
                }
            }
        }
    }

    companion object {
        /** Name eines neuen Filaments: "<Marke> <Typ>", sonst "Filament". */
        fun defaultName(data: ItemJsonData?): String =
            listOfNotNull(data?.brand?.trim(), data?.type?.trim()).filter { it.isNotEmpty() }.joinToString(" ").ifEmpty { "Filament" }
    }
}
