using System.Text.Json;
using Filomat.Api.Domain;

namespace Filomat.Api.Printers;

/// <summary>
/// Snapmaker U1 (mit paxx12-Firmware). Die Druckhistorie kommt wie bei jedem Moonraker-Drucker,
/// zusätzlich meldet der Drucker in <c>filament_detect</c> die Tag-UID pro Werkzeugkopf und in
/// <c>print_task_config</c>, welcher Slicer-Slot auf welchem Kopf druckt.
/// </summary>
public class SnapmakerU1Adapter(HttpClient http) : MoonrakerAdapter(http)
{
    public override PrinterType Type => PrinterType.SnapmakerU1;

    public override async Task<ToolState?> GetToolStateAsync(Printer printer, CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync(printer, "printer/objects/query?filament_detect&print_task_config", cancellationToken);
        if (!json.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("status", out var status))
            return null;
        return ReadToolState(status);
    }

    protected override string ExtraStatusObjects => "&filament_detect&print_task_config";

    protected override ToolState? ReadToolState(JsonElement status)
    {
        var tags = new List<string?>();
        var filaments = new List<DetectedFilament?>();
        if (status.TryGetProperty("filament_detect", out var detect)
            && detect.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Array)
        {
            foreach (var channel in info.EnumerateArray())
            {
                tags.Add(channel.TryGetProperty("CARD_UID", out var uid) ? ToHex(uid) : null);
                filaments.Add(ReadFilament(channel));
            }
        }

        var map = new List<int>();
        if (status.TryGetProperty("print_task_config", out var config)
            && config.TryGetProperty("extruder_map_table", out var table) && table.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in table.EnumerateArray())
                map.Add(entry.TryGetInt32(out var extruder) ? extruder : 0);
        }

        return new ToolState(tags, map, filaments);
    }

    /// <summary>Tag-Daten eines Kopfes; die Firmware setzt unbekannte Werte auf "NONE" bzw. 0.</summary>
    internal static DetectedFilament? ReadFilament(JsonElement channel)
    {
        if (channel.ValueKind != JsonValueKind.Object) return null;
        string? Text(string name) => channel.GetStringOrNull(name) is { } value
            && !string.IsNullOrWhiteSpace(value) && value != "NONE" ? value.Trim() : null;
        int? Number(string name) => channel.GetDoubleOrNull(name) is { } value && value > 0 ? (int)value : null;

        var type = Text("MAIN_TYPE");
        var color = type is null ? null : Number("RGB_1") is { } rgb ? rgb.ToString("X6") : channel.GetDoubleOrNull("RGB_1") == 0 ? "000000" : null;
        return new DetectedFilament(Text("VENDOR"), type, Text("SUB_TYPE"), color,
            Number("HOTEND_MIN_TEMP"), Number("HOTEND_MAX_TEMP"), Number("BED_TEMP"));
    }

    /// <summary>UID als Byte-Liste → Hex in Großbuchstaben, wie sie auch die Apps speichern.</summary>
    internal static string? ToHex(JsonElement uid)
    {
        if (uid.ValueKind != JsonValueKind.Array) return null;
        var bytes = uid.EnumerateArray().Select(b => b.TryGetInt32(out var v) ? (byte)v : (byte)0).ToArray();
        return bytes.Length == 0 || bytes.All(b => b == 0) ? null : Convert.ToHexString(bytes);
    }
}
