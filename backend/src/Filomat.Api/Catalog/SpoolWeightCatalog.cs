using System.Text.Json;

namespace Filomat.Api.Catalog;

/// <summary>
/// Leergewichte von Spulen pro Hersteller aus der SpoolmanDB (https://github.com/Donkie/SpoolmanDB).
/// Wird beim ersten Zugriff geladen und einen Tag lang zwischengespeichert; ohne Internet bleibt die Liste leer.
/// </summary>
public class SpoolWeightCatalog(HttpClient http, IConfiguration configuration, ILogger<SpoolWeightCatalog> logger)
{
    public const string DefaultUrl = "https://donkie.github.io/SpoolmanDB/filaments.json";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(1);
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyDictionary<string, double> _weights = new Dictionary<string, double>();
    private DateTime _validUntil = DateTime.MinValue;

    public async Task<IReadOnlyDictionary<string, double>> GetAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _validUntil) return _weights;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow < _validUntil) return _weights;
            try
            {
                var url = configuration["Filomat:SpoolWeightCatalogUrl"] ?? DefaultUrl;
                await using var stream = await http.GetStreamAsync(url, cancellationToken);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                _weights = Parse(json.RootElement);
                _validUntil = DateTime.UtcNow + CacheDuration;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "SpoolmanDB nicht erreichbar");
                _validUntil = DateTime.UtcNow + RetryAfterError;
            }
            return _weights;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Pro Hersteller das häufigste Leergewicht seiner Einträge.</summary>
    public static IReadOnlyDictionary<string, double> Parse(JsonElement root)
    {
        var byManufacturer = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Array) return byManufacturer.ToDictionary(e => e.Key, _ => 0d);

        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("manufacturer", out var manufacturer) || manufacturer.ValueKind != JsonValueKind.String
                || !entry.TryGetProperty("spool_weight", out var weight) || weight.ValueKind != JsonValueKind.Number)
                continue;
            var grams = weight.GetDouble();
            if (grams <= 0) continue;
            var name = manufacturer.GetString()!.Trim();
            if (!byManufacturer.TryGetValue(name, out var list)) byManufacturer[name] = list = [];
            list.Add(grams);
        }

        return byManufacturer.ToDictionary(
            e => e.Key,
            e => e.Value.GroupBy(g => g).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key,
            StringComparer.OrdinalIgnoreCase);
    }
}
