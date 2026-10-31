using System.Globalization;
using System.Text.Json.Serialization;
using Filomat.Api.Domain;

namespace Filomat.Api.Printers;

/// <summary>Ein Kopf bzw. Slot: was in der App dort steckt, was der Drucker meldet und was der Druck noch braucht.</summary>
/// <param name="RemainingGrams">Restgewicht der Spule laut App.</param>
/// <param name="NeededGrams">Was der laufende Druck aus diesem Kopf noch braucht.</param>
/// <param name="Enough"><c>false</c>, wenn die Spule laut Restgewicht nicht reicht; <c>null</c>, wenn es nichts zu prüfen gibt.</param>
public record ToolLive(
    int Tool,
    string Place,
    string? SpoolId,
    int? SpoolNumber,
    string? ItemId,
    string? ItemName,
    string? Type,
    string? ColorHex,
    double? RemainingGrams,
    SlotReport? Reported,
    double? NeededGrams,
    bool? Enough);

[JsonConverter(typeof(JsonStringEnumConverter<HintKind>))]
public enum HintKind
{
    /// <summary>Die Spule reicht laut Restgewicht nicht für den Rest des Drucks.</summary>
    SpoolShort,

    /// <summary>Der Drucker erkennt ein anderes Tag als die Spule in der App.</summary>
    TagMismatch,

    /// <summary>Material oder Farbe im Slot passen nicht zur Spule in der App.</summary>
    SlotMismatch,

    /// <summary>Slot laut Drucker leer, in der App steckt eine Spule.</summary>
    SlotEmpty,

    /// <summary>Slot laut Drucker belegt, in der App ist dort nichts eingetragen.</summary>
    SlotUnknown,

    /// <summary>Der Drucker erkennt das Plättchen einer Sorte, aber in der App ist offen, welche ihrer Spulen dort steckt.</summary>
    SpoolUnclear,

    /// <summary>Die Düse heizt außerhalb des Bereichs der eingelegten Sorte.</summary>
    Temperature,

    /// <summary>Zu feucht für das Material in der Box.</summary>
    Humidity,
}

/// <param name="Warning"><c>true</c> für Warnungen, <c>false</c> für Hinweise.</param>
public record Hint(HintKind Kind, bool Warning, int? Tool, string Text);

/// <summary>Spule im Drucker mit ihrer Sorte, wie die App sie führt.</summary>
public record PlacedSpool(Spool Spool, Item Item);

/// <summary>Sorte, deren Plättchen der Drucker an einem Kopf ohne Spule erkennt, mit den Nummern ihrer losen Spulen.</summary>
public record DetectedItem(Item Item, IReadOnlyList<int> LooseNumbers);

/// <summary>Verknüpft den gemeldeten Zustand eines Druckers mit den Spulen der App.</summary>
public static class PrinterInsights
{
    /// <summary>Darunter lohnt keine Warnung.</summary>
    private const double MinNeededGrams = 1;

    /// <summary>Spielraum für die Düsentemperatur, bevor gewarnt wird.</summary>
    private const double TemperatureTolerance = 5;

    /// <summary>Bis hierhin gilt eine zu niedrige Solltemperatur als Vorheizen (Prusa misst bei 170 °C das Bett ab), nicht als falsches Profil.</summary>
    private const double PreheatMax = 180;

    /// <summary>Ab diesem RGB-Abstand gilt eine Farbe als anders.</summary>
    private const double ColorDistance = 150;

    /// <summary>Nimmt Feuchte schnell auf und sollte trocken lagern.</summary>
    private static readonly string[] Hygroscopic = ["PETG", "TPU", "TPE", "PA", "NYLON", "PC", "PVA", "BVOH", "PPS", "PEBA"];

    public const double HygroscopicLimit = 40;
    public const double DefaultLimit = 55;

    /// <param name="detected">Pro Kopf ohne Spule die Sorte, deren Plättchen der Drucker dort erkennt.</param>
    public static (List<ToolLive> Tools, List<Hint> Hints) Build(PrinterType type, int toolCount, PrinterStatus status,
        IReadOnlyList<PlacedSpool> placed, IReadOnlyDictionary<int, DetectedItem>? detected = null)
    {
        var tools = new List<ToolLive>();
        var hints = new List<Hint>();
        var count = Math.Max(toolCount, status.Slots?.Count ?? 0);

        for (var tool = 0; tool < count; tool++)
        {
            var place = PlaceName(type, count, tool);
            var spool = placed.FirstOrDefault(p => (p.Spool.Tool ?? 0) == tool);
            var reported = status.Slots?.FirstOrDefault(s => s.Tool == tool);
            var found = spool is null ? detected?.GetValueOrDefault(tool) : null;
            var item = spool?.Item ?? found?.Item;
            var itemType = item?.ItemData?.Type;
            var itemColor = Normalize(item?.ItemData?.ColorHex);
            var label = spool is null ? null : $"{spool.Item.Name} #{spool.Spool.Number}";

            double? needed = null;
            bool? enough = null;
            if (status.IsActive && status.JobGramsPerTool is { } planned && tool < planned.Count && planned[tool] > 0)
            {
                var rest = planned[tool] * (1 - Math.Clamp(status.Progress ?? 0, 0, 100) / 100);
                if (rest >= MinNeededGrams)
                {
                    needed = Math.Round(rest, 1);
                    if (spool?.Spool.RemainingWeight is { } remaining)
                    {
                        enough = remaining >= rest;
                        if (enough == false)
                            hints.Add(new Hint(HintKind.SpoolShort, true, tool,
                                Prefix(place) + (status.JobGramsEstimated
                                    ? L.F("{0} hat noch {1}, der Druck braucht geschätzt noch {2}.", label, Grams(remaining), Grams(rest))
                                    : L.F("{0} hat noch {1}, der Druck braucht noch {2}.", label, Grams(remaining), Grams(rest)))));
                    }
                }
            }

            if (reported is not null)
            {
                if (reported.TagUid is { } tag && spool is not null && !spool.Item.AllTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    hints.Add(new Hint(HintKind.TagMismatch, true, tool, Prefix(place) + L.F("Der Drucker erkennt ein anderes Tag als {0}.", label)));
                else if (reported.Loaded == false && spool is not null)
                    hints.Add(new Hint(HintKind.SlotEmpty, true, tool, Prefix(place) + L.F("Laut Drucker leer, in der App steckt dort {0}.", label)));
                else if (found is not null)
                    hints.Add(new Hint(HintKind.SpoolUnclear, true, tool, found.LooseNumbers.Count > 0
                        ? Prefix(place) + L.F("{0} erkannt. Welche Spule steckt dort? Lose liegen {1}.", found.Item.Name, string.Join(", ", found.LooseNumbers.Select(n => $"#{n}")))
                        : Prefix(place) + L.F("{0} erkannt, in der App steckt dort keine Spule.", found.Item.Name)));
                else if (reported.Loaded == true && spool is null && reported.Type is not null)
                    hints.Add(new Hint(HintKind.SlotUnknown, false, tool,
                        Prefix(place) + L.F("{0} eingelegt, in der App ist dort nichts eingetragen.", Describe(reported.Type, reported.ColorHex))));
                else if (reported.Loaded != false && spool is not null && reported.TagUid is null)
                {
                    if (reported.Type is not null && itemType is not null && BaseType(reported.Type) != BaseType(itemType))
                        hints.Add(new Hint(HintKind.SlotMismatch, true, tool,
                            Prefix(place) + L.F("Der Drucker meldet {0}, in der App steckt dort {1} ({2}).", reported.Type, label, itemType)));
                    else if (Distance(Normalize(reported.ColorHex), itemColor) > ColorDistance)
                        hints.Add(new Hint(HintKind.SlotMismatch, false, tool,
                            Prefix(place) + L.F("Die Farbe im Drucker weicht von {0} ab.", label)));
                }
            }

            tools.Add(new ToolLive(tool, place, spool?.Spool.Id, spool?.Spool.Number, item?.Id, item?.Name,
                itemType ?? reported?.Type, itemColor ?? Normalize(reported?.ColorHex), spool?.Spool.RemainingWeight, reported, needed, enough));
        }

        if (status.State == PrintState.Printing)
            hints.AddRange(TemperatureHints(status, tools, placed));
        hints.AddRange(HumidityHints(status, tools));
        return (tools, hints);
    }

    /// <summary>Solltemperatur jeder Düse gegen den Bereich der Sorte, die gerade aus ihr druckt.</summary>
    private static IEnumerable<Hint> TemperatureHints(PrinterStatus status, List<ToolLive> tools, IReadOnlyList<PlacedSpool> placed)
    {
        var nozzles = status.Heaters?.Where(h => h.Tool is not null).ToList() ?? [];
        foreach (var heater in nozzles)
        {
            if (heater.Target is not { } target || target <= 0) continue;
            // Eine Düse mit mehreren Slots (MMU, AMS, ACE): es zählt der Slot, der gerade fördert
            var tool = nozzles.Count > 1 ? heater.Tool!.Value : status.ActiveTool ?? 0;
            if (placed.FirstOrDefault(p => (p.Spool.Tool ?? 0) == tool) is not { } spool) continue;
            var min = Number(spool.Item.ItemData?.MinTemp);
            var max = Number(spool.Item.ItemData?.MaxTemp);
            if (min is null && max is null) continue;
            var tooCold = target < (min ?? double.MinValue) - TemperatureTolerance && target > PreheatMax;
            if (tooCold || target > (max ?? double.MaxValue) + TemperatureTolerance)
            {
                var place = tools.FirstOrDefault(t => t.Tool == tool)?.Place ?? "";
                var range = min is not null && max is not null ? $"{min:0}–{max:0} °C" : min is not null ? L.F("mindestens {0:0} °C", min) : L.F("höchstens {0:0} °C", max);
                yield return new Hint(HintKind.Temperature, true, tool,
                    Prefix(place) + L.F("{0} heizt auf {1:0} °C, {2} #{3} will {4}. Falsches Profil?", L.T(heater.Name), target, spool.Item.Name, spool.Spool.Number, range));
            }
        }
    }

    /// <summary>Feuchte pro Box gegen das empfindlichste Material darin.</summary>
    private static IEnumerable<Hint> HumidityHints(PrinterStatus status, List<ToolLive> tools)
    {
        foreach (var dryer in status.Dryers ?? [])
        {
            // Was laut Drucker in der Box liegt, sonst was die App dort führt
            var materials = dryer.Tools.Select(t => tools.FirstOrDefault(x => x.Tool == t) is { } tool ? tool.Reported?.Type ?? tool.Type : null)
                .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var sensitive = materials.Where(IsHygroscopic).ToList();
            if (dryer.HumidityPercent is { } humidity)
            {
                var limit = sensitive.Count > 0 ? HygroscopicLimit : DefaultLimit;
                if (humidity > limit)
                    yield return new Hint(HintKind.Humidity, sensitive.Count > 0, null, sensitive.Count > 0
                        ? L.F("{0}: {1:0} % Feuchte, {2} sollte trocknen.", dryer.Name, humidity, string.Join(", ", sensitive))
                        : L.F("{0}: {1:0} % Feuchte, das ist viel für Filament.", dryer.Name, humidity));
            }
            // Bambu ohne Prozentwert: Stufe 1 und 2 sind feucht
            else if (dryer.HumidityLevel is int level and <= 2)
            {
                yield return new Hint(HintKind.Humidity, sensitive.Count > 0, null,
                    sensitive.Count > 0
                        ? L.F("{0}: Feuchtestufe {1} von 5, {2} sollte trocknen. Trockenmittel wechseln?", dryer.Name, level, string.Join(", ", sensitive))
                        : L.F("{0}: Feuchtestufe {1} von 5. Trockenmittel wechseln?", dryer.Name, level));
            }
        }
    }

    public static bool IsHygroscopic(string type) => Hygroscopic.Contains(BaseType(type));

    /// <summary>"Kopf 2", "Slot 3" oder leer bei einem Kopf.</summary>
    public static string PlaceName(PrinterType type, int toolCount, int tool) =>
        toolCount <= 1 ? "" : type is PrinterType.PrusaLink or PrinterType.Anycubic or PrinterType.Bambu ? $"Slot {tool + 1}" : L.F("Kopf {0}", tool + 1);

    private static string Prefix(string place) => place.Length > 0 ? $"{place}: " : "";

    private static string Grams(double grams) => $"{Math.Round(grams):0} g";

    private static string Describe(string type, string? color) => color is null ? type : $"{type} (#{color})";

    /// <summary>Grundmaterial: "PLA+", "PLA Matte", "pla" → "PLA".</summary>
    public static string BaseType(string type)
    {
        var letters = new string(type.Trim().TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        return letters.Length > 0 ? letters : type.Trim().ToUpperInvariant();
    }

    private static string? Normalize(string? hex)
    {
        var value = hex?.Trim().TrimStart('#').ToUpperInvariant();
        return value is { Length: >= 6 } && value[..6].All(Uri.IsHexDigit) ? value[..6] : null;
    }

    private static double Distance(string? a, string? b)
    {
        if (a is null || b is null) return 0;
        static (int R, int G, int B) Rgb(string hex) => (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
        var (r1, g1, b1) = Rgb(a);
        var (r2, g2, b2) = Rgb(b);
        return Math.Sqrt(Math.Pow(r1 - r2, 2) + Math.Pow(g1 - g2, 2) + Math.Pow(b1 - b2, 2));
    }

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
}
