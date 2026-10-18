import Foundation
#if canImport(CoreNFC) && !targetEnvironment(simulator)
import CoreNFC
#endif

/// Ergebnis eines Tag-Scans.
struct ScannedTag: Equatable {
    /// Kennung des Tags: die UID als Hex-String (wie `bytesToHex(tag.id)` in der Android-App)
    /// bzw. der Text-Payload, falls der Tag einen Nicht-JSON-Record enthält.
    var tagId: String
    /// Filament-Daten, falls der Tag einen `application/json`-Record enthält.
    var itemData: ItemJsonData?
    /// Gesetzt, wenn ein JSON-Record vorhanden, aber nicht lesbar war.
    var parseError: String?
    /// UID, Tag-Typ und alle NDEF-Records, für die Anzeige "Tag lesen".
    var details: TagDetails?
}

enum NFCError: LocalizedError, Equatable {
    case unavailable
    case cancelled
    case failed(String)

    var errorDescription: String? {
        switch self {
        case .unavailable: return "NFC ist auf diesem Gerät nicht verfügbar."
        case .cancelled: return "Scan abgebrochen."
        case .failed(let message): return message
        }
    }
}

/// NFC-Zugriff über CoreNFC. Jeder Aufruf öffnet das System-Scanfenster und schließt es danach wieder.
@MainActor
final class NFCService {
    static let shared = NFCService()

    static var isAvailable: Bool {
        #if canImport(CoreNFC) && !targetEnvironment(simulator)
        return NFCTagReaderSession.readingAvailable
        #else
        return false
        #endif
    }

    #if canImport(CoreNFC) && !targetEnvironment(simulator)
    private var activeSession: TagSession?
    #endif

    /// Liest einen Tag: UID und – falls vorhanden – die Filament-Daten.
    func scan(prompt: String) async throws -> ScannedTag {
        #if canImport(CoreNFC) && !targetEnvironment(simulator)
        let result: TagSession.Outcome = try await run(prompt: prompt, operation: .read)
        guard case .read(let tag) = result else { throw NFCError.failed("Unerwartetes Ergebnis") }
        return tag
        #else
        throw NFCError.unavailable
        #endif
    }

    /// Schreibt JSON als `application/json`-Record auf einen Tag und liefert dessen Kennung (UID).
    /// `validate` erhält die UID vor dem Schreiben; eine zurückgegebene Meldung bricht den Vorgang ab.
    func writeJSON(
        _ json: Data,
        prompt: String,
        validate: @escaping @MainActor (String) -> String? = { _ in nil }
    ) async throws -> String {
        #if canImport(CoreNFC) && !targetEnvironment(simulator)
        let result: TagSession.Outcome = try await run(prompt: prompt, operation: .writeJSON(json, validate: validate))
        guard case .written(let tagId) = result else { throw NFCError.failed("Unerwartetes Ergebnis") }
        return tagId
        #else
        throw NFCError.unavailable
        #endif
    }

    #if canImport(CoreNFC) && !targetEnvironment(simulator)
    private func run(prompt: String, operation: TagSession.Operation) async throws -> TagSession.Outcome {
        guard Self.isAvailable else { throw NFCError.unavailable }
        activeSession?.cancel()
        let session = TagSession(operation: operation)
        activeSession = session
        defer { if activeSession === session { activeSession = nil } }
        return try await session.start(prompt: prompt)
    }
    #endif
}

#if canImport(CoreNFC) && !targetEnvironment(simulator)

/// Kapselt eine `NFCTagReaderSession` und übersetzt die Delegate-Callbacks in `async`/`await`.
private final class TagSession: NSObject, NFCTagReaderSessionDelegate {
    enum Operation {
        case read
        case writeJSON(Data, validate: @MainActor (String) -> String?)
    }

    enum Outcome {
        case read(ScannedTag)
        case written(String)
    }

    private static let jsonMimeType = "application/json"

    private let operation: Operation
    private var session: NFCTagReaderSession?
    private var continuation: CheckedContinuation<Outcome, Error>?

    init(operation: Operation) {
        self.operation = operation
    }

    @MainActor
    func start(prompt: String) async throws -> Outcome {
        try await withCheckedThrowingContinuation { continuation in
            self.continuation = continuation
            let created: NFCTagReaderSession? = NFCTagReaderSession(
                pollingOption: [.iso14443, .iso15693],
                delegate: self,
                queue: .main
            )
            guard let session = created else {
                finish(.failure(NFCError.unavailable))
                return
            }
            self.session = session
            session.alertMessage = prompt
            session.begin()
        }
    }

    func cancel() {
        session?.invalidate()
        finish(.failure(NFCError.cancelled))
    }

    private func finish(_ result: Result<Outcome, Error>) {
        guard let continuation else { return }
        self.continuation = nil
        continuation.resume(with: result)
    }

    private func fail(_ session: NFCTagReaderSession, _ message: String) {
        session.invalidate(errorMessage: message)
        finish(.failure(NFCError.failed(message)))
    }

    // MARK: NFCTagReaderSessionDelegate

    func tagReaderSessionDidBecomeActive(_ session: NFCTagReaderSession) {}

    func tagReaderSession(_ session: NFCTagReaderSession, didInvalidateWithError error: Error) {
        if let readerError = error as? NFCReaderError,
           readerError.code == .readerSessionInvalidationErrorUserCanceled {
            finish(.failure(NFCError.cancelled))
        } else {
            finish(.failure(NFCError.failed(error.localizedDescription)))
        }
    }

    func tagReaderSession(_ session: NFCTagReaderSession, didDetect tags: [NFCTag]) {
        guard tags.count == 1, let tag = tags.first else {
            session.alertMessage = "Mehrere Tags erkannt. Bitte nur ein Tag an das Gerät halten."
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { session.restartPolling() }
            return
        }

        let ndefTag: NFCNDEFTag
        let identifier: Data
        let technology: String
        switch tag {
        case .miFare(let t):
            ndefTag = t
            identifier = t.identifier
            switch t.mifareFamily {
            case .ultralight: technology = "MIFARE Ultralight / NTAG"
            case .plus: technology = "MIFARE Plus"
            case .desfire: technology = "MIFARE DESFire"
            default: technology = "MIFARE / ISO 14443"
            }
        case .iso15693(let t):
            ndefTag = t
            identifier = t.identifier
            technology = "ISO 15693 (NFC-V)"
        case .iso7816(let t):
            ndefTag = t
            identifier = t.identifier
            technology = "ISO 7816"
        case .feliCa(let t):
            ndefTag = t
            identifier = t.currentIDm
            technology = "FeliCa"
        @unknown default:
            fail(session, "Dieser Tag-Typ wird nicht unterstützt.")
            return
        }
        let tagId = identifier.map { String(format: "%02X", $0) }.joined()

        if case .writeJSON(_, let validate) = operation,
           // Die Session wurde mit der Main-Queue als Delegate-Queue erstellt.
           let message = MainActor.assumeIsolated({ validate(tagId) }) {
            fail(session, message)
            return
        }

        session.connect(to: tag) { [weak self] error in
            guard let self else { return }
            if let error {
                self.fail(session, "Verbindung zum Tag fehlgeschlagen: \(error.localizedDescription)")
                return
            }
            switch self.operation {
            case .read:
                self.read(ndefTag, tagId: tagId, technology: technology, session: session)
            case .writeJSON(let json, _):
                self.write(json, to: ndefTag, tagId: tagId, session: session)
            }
        }
    }

    // MARK: Lesen / Schreiben

    private func read(_ tag: NFCNDEFTag, tagId: String, technology: String, session: NFCTagReaderSession) {
        tag.queryNDEFStatus { [weak self] status, capacity, error in
            guard let self else { return }
            var details = TagDetails(uid: tagId, technology: technology)
            if error == nil {
                switch status {
                case .readWrite: details.ndefStatus = "beschreibbar"
                case .readOnly: details.ndefStatus = "schreibgeschützt"
                case .notSupported: details.ndefStatus = "kein NDEF"
                @unknown default: break
                }
                if status != .notSupported { details.capacity = capacity }
            }
            // Tag ohne NDEF: wie in der Android-App die UID verwenden
            guard error == nil, status != .notSupported else {
                self.complete(session, with: ScannedTag(tagId: tagId, details: details))
                return
            }
            tag.readNDEF { [details] message, _ in
                var details = details
                details.records = message?.records.map {
                    TagRecord(
                        format: UInt8($0.typeNameFormat.rawValue),
                        type: String(data: $0.type, encoding: .utf8) ?? "",
                        payload: $0.payload
                    )
                } ?? []
                // Leerer Tag oder Lesefehler: ebenfalls die UID verwenden
                guard let record = message?.records.first else {
                    self.complete(session, with: ScannedTag(tagId: tagId, details: details))
                    return
                }
                let type = String(data: record.type, encoding: .utf8)
                if record.typeNameFormat == .media, type == Self.jsonMimeType {
                    do {
                        let data = try JSONDecoder().decode(ItemJsonData.self, from: record.payload)
                        self.complete(session, with: ScannedTag(tagId: tagId, itemData: data, details: details))
                    } catch {
                        self.complete(session, with: ScannedTag(
                            tagId: tagId,
                            parseError: "Fehler beim Parsen der JSON-Daten: \(error.localizedDescription)",
                            details: details
                        ))
                    }
                } else {
                    // Sonstige Records: Payload als Kennung, wie in der Android-App
                    let text = String(data: record.payload, encoding: .utf8) ?? tagId
                    self.complete(session, with: ScannedTag(tagId: text, details: details))
                }
            }
        }
    }

    private func write(_ json: Data, to tag: NFCNDEFTag, tagId: String, session: NFCTagReaderSession) {
        tag.queryNDEFStatus { [weak self] status, capacity, error in
            guard let self else { return }
            if let error {
                self.fail(session, "Fehler beim Schreiben: \(error.localizedDescription)")
                return
            }
            switch status {
            case .notSupported:
                self.fail(session, "Tag unterstützt NDEF nicht")
                return
            case .readOnly:
                self.fail(session, "Tag ist nicht beschreibbar")
                return
            case .readWrite:
                break
            @unknown default:
                self.fail(session, "Unbekannter Tag-Status")
                return
            }

            let record = NFCNDEFPayload(
                format: .media,
                type: Data(Self.jsonMimeType.utf8),
                identifier: Data(),
                payload: json
            )
            let message = NFCNDEFMessage(records: [record])
            if message.length > capacity {
                self.fail(session, "Tag zu klein: benötigt \(message.length) Bytes, verfügbar: \(capacity) Bytes")
                return
            }

            tag.writeNDEF(message) { error in
                if let error {
                    self.fail(session, "Fehler beim Schreiben: \(error.localizedDescription)")
                    return
                }
                session.alertMessage = "Tag erfolgreich beschrieben!"
                session.invalidate()
                self.finish(.success(.written(tagId)))
            }
        }
    }

    private func complete(_ session: NFCTagReaderSession, with tag: ScannedTag) {
        session.alertMessage = "Tag gelesen"
        session.invalidate()
        finish(.success(.read(tag)))
    }
}

#endif
