import Foundation

struct FilamentPreset: Codable, Hashable {
    var type: String
    var brand: String?
    var minTemp: String
    var maxTemp: String
    var bedMinTemp: String?
    var bedMaxTemp: String?

    enum CodingKeys: String, CodingKey {
        case type
        case brand
        case minTemp = "min_temp"
        case maxTemp = "max_temp"
        case bedMinTemp = "bed_min_temp"
        case bedMaxTemp = "bed_max_temp"
    }
}

/// Temperatur-Presets, Marken und Varianten aus den gebündelten JSON-Dateien
/// (dieselben Dateien wie in `android-app/app/src/main/assets`).
struct FilamentPresetDatabase {
    static let filamentTypes = ["PLA", "TPU", "PETG", "ASA", "ABS"]
    static let shared = FilamentPresetDatabase()

    let presets: [FilamentPreset]
    let brands: [String]
    let variants: [String]

    init(bundle: Bundle = .main) {
        let loadedPresets: [FilamentPreset]? = Self.load("presets", from: bundle)
        presets = loadedPresets ?? Self.fallbackPresets
        let loadedBrands: [String]? = Self.load("brands", from: bundle)
        let loadedVariants: [String]? = Self.load("variants", from: bundle)
        brands = Self.uniqueSorted(loadedBrands ?? [])
        variants = Self.uniqueSorted(loadedVariants ?? [])
    }

    init(presets: [FilamentPreset], brands: [String], variants: [String]) {
        self.presets = presets
        self.brands = Self.uniqueSorted(brands)
        self.variants = Self.uniqueSorted(variants)
    }

    /// Preset für Typ und Marke; ohne Treffer für die Marke wird das Standard-Preset des Typs geliefert.
    func preset(type: String, brand: String?) -> FilamentPreset? {
        if let brand, !brand.isEmpty,
           let match = presets.first(where: { $0.type == type && $0.brand == brand }) {
            return match
        }
        return presets.first { $0.type == type && $0.brand == nil }
    }

    private static func load<T: Decodable>(_ name: String, from bundle: Bundle) -> T? {
        guard let url = bundle.url(forResource: name, withExtension: "json")
                ?? bundle.url(forResource: name, withExtension: "json", subdirectory: "Resources"),
              let data = try? Data(contentsOf: url) else {
            print("FilamentPresetDatabase: \(name).json nicht gefunden")
            return nil
        }
        do {
            return try JSONDecoder().decode(T.self, from: data)
        } catch {
            print("FilamentPresetDatabase: Fehler beim Laden von \(name).json: \(error)")
            return nil
        }
    }

    private static func uniqueSorted(_ values: [String]) -> [String] {
        Array(Set(values)).sorted()
    }

    private static let fallbackPresets: [FilamentPreset] = [
        FilamentPreset(type: "PLA", brand: nil, minTemp: "190", maxTemp: "220", bedMinTemp: "50", bedMaxTemp: "60"),
        FilamentPreset(type: "PETG", brand: nil, minTemp: "220", maxTemp: "250", bedMinTemp: "70", bedMaxTemp: "80"),
        FilamentPreset(type: "TPU", brand: nil, minTemp: "220", maxTemp: "250", bedMinTemp: "40", bedMaxTemp: "60"),
        FilamentPreset(type: "ABS", brand: nil, minTemp: "230", maxTemp: "260", bedMinTemp: "80", bedMaxTemp: "100"),
        FilamentPreset(type: "ASA", brand: nil, minTemp: "240", maxTemp: "270", bedMinTemp: "80", bedMaxTemp: "100"),
    ]
}
