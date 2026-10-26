using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentFTP;
using MQTTnet;
using MQTTnet.Formatter;

namespace Filomat.Api.Printers;

/// <summary>Was der Drucker auf <c>pushall</c> meldet.</summary>
/// <param name="Print">Inhalt von <c>print</c>: Zustand, laufender Druck, AMS.</param>
/// <param name="Serial">Seriennummer, unter der der Drucker meldet.</param>
public record BambuReport(JsonElement Print, string Serial);

/// <summary>Filament eines Slicer-Slots laut <c>Metadata/slice_info.config</c> der 3MF-Datei.</summary>
/// <param name="Id">Slicer-Slot ab 1.</param>
/// <param name="Color">Farbe als <c>RRGGBB</c>, Großbuchstaben.</param>
public record SlicedFilament(int Id, string? Type, string? Color, double UsedGrams, double UsedMeters);

/// <summary>Fragt einen Bambu-Drucker im LAN ab.</summary>
public interface IBambuLan
{
    /// <param name="serial">Seriennummer; ohne Angabe die aus dem Zertifikat des Druckers.</param>
    Task<BambuReport> QueryAsync(string baseUrl, string accessCode, string? serial, CancellationToken cancellationToken);

    /// <summary>Lädt die erste vorhandene der Dateien von der SD-Karte; <c>null</c>, wenn keine da ist.</summary>
    Task<byte[]?> DownloadAsync(string baseUrl, string accessCode, IReadOnlyList<string> paths, CancellationToken cancellationToken);
}

/// <summary>
/// Bambu Lab P1S, P1P, X1, A1 im LAN: MQTT über TLS auf Port 8883 (Benutzer <c>bblp</c>, Passwort = Zugangscode),
/// Themen <c>device/{seriennummer}/report</c> und <c>…/request</c>. Die P1-Serie schickt nur Änderungen; den
/// vollständigen Zustand gibt es auf <c>pushall</c>. Daher pro Abgleich: verbinden, fragen, trennen. Die
/// Seriennummer steht im Zertifikat des Druckers. Die Druckdatei (3MF mit den Slicer-Gewichten) liegt auf der
/// SD-Karte, erreichbar per FTPS (implizites TLS, Port 990).
/// </summary>
public class BambuLan : IBambuLan
{
    public const int MqttPort = 8883;
    public const int FtpPort = 990;
    public const string User = "bblp";
    private static readonly TimeSpan ReportTimeout = TimeSpan.FromSeconds(10);

    public async Task<BambuReport> QueryAsync(string baseUrl, string accessCode, string? serial, CancellationToken cancellationToken)
    {
        var address = Address(baseUrl);
        string? certificateName = null;
        var merged = new JsonObject();
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var client = new MqttClientFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += e =>
        {
            try
            {
                if (JsonNode.Parse(e.ApplicationMessage.ConvertPayloadToString()) is not JsonObject { } root
                    || root["print"] is not JsonObject print)
                    return Task.CompletedTask;
                lock (merged)
                {
                    // Änderungen ergänzen den Stand; der vollständige kommt auf pushall. Erst auf ihn warten: eine
                    // Änderung mit gcode_state und ams, aber ohne Fortschritt und Datei, ließ einen fertigen Druck
                    // wie abgebrochen aussehen.
                    foreach (var (key, value) in print) merged[key] = value?.DeepClone();
                    if (IsFullReport(print)) complete.TrySetResult();
                }
            }
            catch (JsonException)
            {
            }
            return Task.CompletedTask;
        };

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(address.Host, address.IsDefaultPort ? MqttPort : address.Port)
            .WithCredentials(User, accessCode)
            .WithClientId($"filomat-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}")
            .WithProtocolVersion(MqttProtocolVersion.V311)
            // Zertifikat der Bambu-CA, ausgestellt auf die Seriennummer; die Verbindung bleibt im LAN
            .WithTlsOptions(tls => tls.UseTls().WithCertificateValidationHandler(context =>
            {
                if (context.Certificate is { } certificate)
                    certificateName = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()).GetNameInfo(X509NameType.SimpleName, false);
                return true;
            }))
            .WithTimeout(TimeSpan.FromSeconds(15))
            .Build();

        try
        {
            await client.ConnectAsync(options, cancellationToken);
            var device = string.IsNullOrWhiteSpace(serial) ? certificateName : serial.Trim();
            if (string.IsNullOrEmpty(device))
                throw new PrinterUnavailableException("Seriennummer unbekannt; bitte als Seriennummer:Zugangscode eintragen.");

            await client.SubscribeAsync($"device/{device}/report", cancellationToken: cancellationToken);
            await client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic($"device/{device}/request")
                .WithPayload("""{"pushing":{"sequence_id":"0","command":"pushall","version":1,"push_target":1}}""")
                .Build(), cancellationToken);

            try
            {
                await complete.Task.WaitAsync(ReportTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Ohne AMS fehlt "ams" womöglich ganz; dann reicht der Zustand
                lock (merged)
                {
                    if (!merged.ContainsKey("gcode_state")) throw;
                }
            }
            await client.DisconnectAsync(cancellationToken: CancellationToken.None);
            lock (merged)
            {
                return new BambuReport(JsonSerializer.SerializeToElement(merged), device);
            }
        }
        catch (TimeoutException)
        {
            throw new PrinterUnavailableException("Der Drucker antwortet nicht auf MQTT-Abfragen (Seriennummer falsch?).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not PrinterUnavailableException)
        {
            // Verbindungsfehler, falscher Zugangscode, TLS
            throw new PrinterUnavailableException($"MQTT-Verbindung zum Drucker fehlgeschlagen: {Describe(ex)}", ex);
        }
    }

    /// <summary>
    /// Vollständiger Zustand: die Antwort auf pushall (<c>msg</c> 0, Änderungen haben 1) oder eine Meldung mit Zustand,
    /// Fortschritt und Datei.
    /// </summary>
    public static bool IsFullReport(JsonObject print) =>
        print["msg"] is JsonValue msg && msg.ToString() == "0"
        || print.ContainsKey("gcode_state") && print.ContainsKey("mc_percent") && (print.ContainsKey("gcode_file") || print.ContainsKey("subtask_name"));

    /// <summary>Meldung samt Ursachen; MQTTnet meldet außen nur "Error while connecting host".</summary>
    public static string Describe(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (!messages.Contains(e.Message)) messages.Add(e.Message.TrimEnd('.'));
        return string.Join(" → ", messages) + ".";
    }

    public async Task<byte[]?> DownloadAsync(string baseUrl, string accessCode, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        await using var ftp = new AsyncFtpClient(Address(baseUrl).Host, User, accessCode, FtpPort);
        ftp.Config.EncryptionMode = FtpEncryptionMode.Implicit;
        ftp.Config.DataConnectionEncryption = true;
        // Selbst signiert, auf die Seriennummer ausgestellt
        ftp.Config.ValidateAnyCertificate = true;
        ftp.Config.ConnectTimeout = 10000;
        await ftp.Connect(cancellationToken);
        foreach (var path in paths)
        {
            if (!await ftp.FileExists(path, cancellationToken)) continue;
            return await ftp.DownloadBytes(path, cancellationToken);
        }
        return null;
    }

    /// <summary>Adresse des Druckers, mit oder ohne <c>http://</c>.</summary>
    public static Uri Address(string baseUrl) => new(baseUrl.Contains("://") ? baseUrl : $"http://{baseUrl}");

    /// <summary>Wo der Drucker die Datei eines Drucks ablegt: aus Bambu Studio im LAN auf der SD-Karte, aus der Cloud in <c>/cache</c>.</summary>
    public static IReadOnlyList<string> FileCandidates(string? gcodeFile, string? subtaskName)
    {
        var paths = new List<string>();
        if (gcodeFile is { Length: > 0 } && gcodeFile.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
        {
            var path = "/" + gcodeFile.TrimStart('/');
            foreach (var prefix in new[] { "/mnt/sdcard", "/sdcard" })
                if (path.StartsWith(prefix + "/", StringComparison.Ordinal)) path = path[prefix.Length..];
            paths.Add(path);
        }
        if (subtaskName is { Length: > 0 })
        {
            foreach (var folder in new[] { "/", "/cache/" })
            foreach (var extension in new[] { ".gcode.3mf", ".3mf" })
                paths.Add(subtaskName.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) ? folder + subtaskName : folder + subtaskName + extension);
        }
        return paths.Distinct().ToList();
    }

    /// <summary>Plattennummer aus <c>gcode_file</c> (<c>…/plate_2.gcode</c>), sonst <c>null</c>.</summary>
    public static int? PlateOf(string? gcodeFile) =>
        gcodeFile is not null && Regex.Match(gcodeFile, @"plate_(\d+)\.gcode", RegexOptions.IgnoreCase) is { Success: true } match
            ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// Filamente einer Platte aus <c>Metadata/slice_info.config</c>. Bambu Studio packt beim Drucken meist nur die
    /// gedruckte Platte in die Datei; sonst zählt die Platte aus <paramref name="plate"/>, ohne Angabe die erste.
    /// </summary>
    public static IReadOnlyList<SlicedFilament>? ReadSliceInfo(byte[] threeMf, int? plate)
    {
        using var zip = new ZipArchive(new MemoryStream(threeMf), ZipArchiveMode.Read);
        var entry = zip.GetEntry("Metadata/slice_info.config");
        if (entry is null) return null;
        using var stream = entry.Open();
        var plates = XDocument.Load(stream).Root?.Elements("plate").ToList() ?? [];
        var chosen = plates.FirstOrDefault(p => plate is not null && p.Elements("metadata")
                         .Any(m => (string?)m.Attribute("key") == "index" && (string?)m.Attribute("value") == plate.Value.ToString(CultureInfo.InvariantCulture)))
                     ?? plates.FirstOrDefault();
        if (chosen is null) return null;

        return chosen.Elements("filament").Select(f => new SlicedFilament(
            (int)Number(f, "id"),
            (string?)f.Attribute("type"),
            ((string?)f.Attribute("color"))?.TrimStart('#').ToUpperInvariant() is { Length: >= 6 } color ? color[..6] : null,
            Number(f, "used_g"),
            Number(f, "used_m"))).ToList();
    }

    /// <summary>Vorschaubild der Platte (<c>Metadata/plate_N.png</c>), ohne Angabe der ersten vorhandenen.</summary>
    public static byte[]? ReadPlateImage(byte[] threeMf, int? plate)
    {
        using var zip = new ZipArchive(new MemoryStream(threeMf), ZipArchiveMode.Read);
        var entry = (plate is not null ? zip.GetEntry($"Metadata/plate_{plate}.png") : null)
                    ?? zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^Metadata/plate_\d+\.png$")).OrderBy(e => e.FullName).FirstOrDefault();
        if (entry is null) return null;
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static double Number(XElement element, string name) =>
        double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
