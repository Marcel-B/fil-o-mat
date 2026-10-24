using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;
using Filomat.Api.Printers;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Filomat.Api.Tests;

/// <summary>Meldungen nach github.com/Doridian/OpenBambuAPI (mqtt.md) und ha-bambulab, gekürzt.</summary>
public class FakeBambu : IBambuLan
{
    public string Print { get; set; } = Report("IDLE", 0);
    public byte[]? File { get; set; }
    public List<IReadOnlyList<string>> Downloads { get; } = [];
    public List<(string Code, string? Serial)> Logins { get; } = [];

    public Task<BambuReport> QueryAsync(string baseUrl, string accessCode, string? serial, CancellationToken cancellationToken)
    {
        Logins.Add((accessCode, serial));
        return Task.FromResult(new BambuReport(JsonDocument.Parse(Print).RootElement.Clone(), serial ?? "01P00A123456789"));
    }

    public Task<byte[]?> DownloadAsync(string baseUrl, string accessCode, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        Downloads.Add(paths);
        return Task.FromResult(File);
    }

    /// <summary>Ein AMS: Fach 0 weiße Bambu-Spule (mit Restmenge), 1 schwarz, 2 rotes PETG, 3 leer.</summary>
    public static string Ams(int trayNow, double whiteRemain = 80) => $$"""
        {"ams":[{"id":"0","humidity":"4","temp":"0.0","tray":[
            {"id":"0","tray_type":"PLA","tray_color":"FFFFFFFF","remain":{{whiteRemain}},"tray_weight":"1000","tray_sub_brands":"PLA Basic"},
            {"id":"1","tray_type":"PLA","tray_color":"000000FF","remain":-1,"tray_weight":"0"},
            {"id":"2","tray_type":"PETG","tray_color":"FF0000FF","remain":-1,"tray_weight":"0"},
            {"id":"3"}]}],
         "ams_exist_bits":"1","tray_exist_bits":"7","tray_now":"{{trayNow}}","tray_pre":"0","tray_tar":"0","version":4}
        """;

    public static string Report(string state, double percent, int trayNow = 255, string name = "benchy", string taskId = "0", string? ams = null) => $$"""
        {"gcode_state":"{{state}}","mc_percent":{{percent}},"mc_remaining_time":12,"subtask_name":"{{name}}",
         "gcode_file":"{{name}}.gcode.3mf","task_id":"{{taskId}}","subtask_id":"0","layer_num":3,"total_layer_num":50,
         "ams":{{ams ?? Ams(trayNow)}},
         "vt_tray":{"id":"254","tray_type":"PETG","tray_color":"00FF00FF","remain":0,"tray_weight":"0"},
         "command":"push_status","msg":0,"sequence_id":"2021"}
        """;

    /// <summary>3MF mit <c>Metadata/slice_info.config</c>, wie Bambu Studio sie schreibt.</summary>
    public static byte[] ThreeMf(params string[] plates)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <config>
              <header><header_item key="X-BBL-Client-Type" value="slicer"/></header>
              {string.Join("\n", plates)}
            </config>
            """;
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("Metadata/slice_info.config").Open());
            writer.Write(xml.Trim());
        }
        return stream.ToArray();
    }

    public static string Plate(int index, params (int Id, string Type, string Color, double Grams, double Meters)[] filaments) => $"""
        <plate>
          <metadata key="index" value="{index}"/>
          <metadata key="weight" value="{filaments.Sum(f => f.Grams).ToString(System.Globalization.CultureInfo.InvariantCulture)}"/>
          {string.Join("\n", filaments.Select(f => FormattableString.Invariant(
              $"""<filament id="{f.Id}" tray_info_idx="GFA00" type="{f.Type}" color="#{f.Color}" used_m="{f.Meters}" used_g="{f.Grams}" />""")))}
        </plate>
        """;
}

public class BambuTests : IDisposable
{
    private readonly FakeBambu _printer = new();
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public BambuTests()
    {
        _factory = new ApiFactory { BambuLan = _printer };
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private async Task<PrinterResponse> CreatePrinterAsync(string accessCode = "12345678")
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("P1S", PrinterType.Bambu, "http://192.168.2.170", accessCode, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>())!;
    }

    private async Task<Item> PlaceSpoolAsync(string tag, string printerId, int tool, string type = "PLA")
    {
        var item = (await (await _client.PostAsJsonAsync("/api/items",
            new ItemRequest(null, tag, $"{type} {tag}", null, null, new FilamentData { Type = type }, 1000))).Content.ReadFromJsonAsync<Item>())!;
        await _client.PostAsJsonAsync($"/api/items/{item.Id}/spools", new OpenSpoolRequest(null, printerId, tool));
        return item;
    }

    private async Task<SyncResponse> SyncAsync(string printerId) =>
        (await (await _client.PostAsync($"/api/printers/{printerId}/sync", null)).Content.ReadFromJsonAsync<SyncResponse>())!;

    private async Task<double> RemainingAsync(string itemId) =>
        Assert.Single((await _client.GetFromJsonAsync<List<Spool>>($"/api/spools?itemId={itemId}"))!).RemainingWeight!.Value;

    private async Task<List<Consumption>> ConsumptionAsync(string printerId) =>
        (await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={printerId}"))!;

    [Fact]
    public async Task BooksTheSlicedWeightOnTheSpoolInTheFeedingSlot()
    {
        _printer.File = FakeBambu.ThreeMf(FakeBambu.Plate(1, (1, "PLA", "000000", 12.5, 4.2)));
        var printer = await CreatePrinterAsync();
        Assert.Equal(5, printer.Tools);
        var black = await PlaceSpoolAsync("B", printer.Id, 1);

        _printer.Print = FakeBambu.Report("PREPARE", 0, trayNow: 1);
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);
        _printer.Print = FakeBambu.Report("RUNNING", 60, trayNow: 1);
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);
        // Am Ende zieht der P1S das Filament zurück
        _printer.Print = FakeBambu.Report("FINISH", 100, trayNow: 255);
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);
        // FINISH bleibt stehen, bis der nächste Druck beginnt
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(1000 - 12.5, await RemainingAsync(black.Id), 2);
        var consumption = Assert.Single(await ConsumptionAsync(printer.Id));
        Assert.Equal(("completed", "benchy", 1, 4200d), (consumption.Status, consumption.JobName, consumption.Tool, consumption.LengthMm));
        // Datei nur einmal geholt, zuerst dort, wo Bambu Studio sie im LAN ablegt
        Assert.Equal("/benchy.gcode.3mf", Assert.Single(_printer.Downloads)[0]);
        Assert.All(_printer.Logins, login => Assert.Equal(("12345678", null), login));
        Assert.Null((await _client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{printer.Id}"))!.LastSyncError);
    }

    [Fact]
    public async Task AssignsEachFilamentToTheSlotWithItsColour()
    {
        _printer.File = FakeBambu.ThreeMf(FakeBambu.Plate(1, (1, "PLA", "FFFFFF", 5, 1.6), (2, "PETG", "FF0000", 3, 1), (3, "PLA", "000000", 0, 0)));
        var printer = await CreatePrinterAsync();
        var white = await PlaceSpoolAsync("W", printer.Id, 0);
        var red = await PlaceSpoolAsync("R", printer.Id, 2, "PETG");

        _printer.Print = FakeBambu.Report("RUNNING", 5, trayNow: 0);
        await SyncAsync(printer.Id);
        _printer.Print = FakeBambu.Report("RUNNING", 50, trayNow: 2);
        await SyncAsync(printer.Id);
        _printer.Print = FakeBambu.Report("FINISH", 100);
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(1000 - 5, await RemainingAsync(white.Id), 2);
        Assert.Equal(1000 - 3, await RemainingAsync(red.Id), 2);
    }

    [Fact]
    public async Task BooksACancelledPrintByProgress()
    {
        _printer.File = FakeBambu.ThreeMf(FakeBambu.Plate(1, (1, "PLA", "000000", 20, 6.7)));
        var printer = await CreatePrinterAsync();
        var black = await PlaceSpoolAsync("B", printer.Id, 1);

        _printer.Print = FakeBambu.Report("RUNNING", 30, trayNow: 1);
        await SyncAsync(printer.Id);
        _printer.Print = FakeBambu.Report("FAILED", 40, trayNow: 1);
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(1000 - 8, await RemainingAsync(black.Id), 2);
        Assert.Equal("cancelled", Assert.Single(await ConsumptionAsync(printer.Id)).Status);
    }

    [Fact]
    public async Task UsesTheAmsRemainderWithoutThePrintFile()
    {
        var printer = await CreatePrinterAsync();
        var white = await PlaceSpoolAsync("W", printer.Id, 0);

        _printer.Print = FakeBambu.Report("RUNNING", 10, ams: FakeBambu.Ams(0, whiteRemain: 80));
        await SyncAsync(printer.Id);
        _printer.Print = FakeBambu.Report("RUNNING", 90, ams: FakeBambu.Ams(0, whiteRemain: 79));
        await SyncAsync(printer.Id);
        _printer.Print = FakeBambu.Report("FINISH", 100, ams: FakeBambu.Ams(255, whiteRemain: 78));
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        // 2 % von 1000 g
        Assert.Equal(1000 - 20, await RemainingAsync(white.Id), 2);
        // Während des Drucks bei jedem Abgleich gesucht, höchstens dreimal
        Assert.Equal(2, _printer.Downloads.Count);
    }

    [Fact]
    public async Task BooksAPrinterWithoutAmsOnItsSpool()
    {
        _printer.File = FakeBambu.ThreeMf(FakeBambu.Plate(1, (1, "PETG", "00FF00", 7, 2.2)));
        var printer = await CreatePrinterAsync();
        var spool = await PlaceSpoolAsync("G", printer.Id, 0, "PETG");
        const string noAms = """{"ams":[],"ams_exist_bits":"0","tray_exist_bits":"0","tray_now":"255"}""";

        _printer.Print = FakeBambu.Report("RUNNING", 50, ams: noAms);
        await SyncAsync(printer.Id);
        _printer.Print = FakeBambu.Report("FINISH", 100, ams: noAms);
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(1, (await _client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{printer.Id}"))!.Tools);
        Assert.Equal(1000 - 7, await RemainingAsync(spool.Id), 2);
    }

    [Fact]
    public async Task FinishesThePreviousPrintWhenANewOneStarts()
    {
        _printer.File = FakeBambu.ThreeMf(FakeBambu.Plate(1, (1, "PLA", "000000", 10, 3.3)));
        var printer = await CreatePrinterAsync();
        var black = await PlaceSpoolAsync("B", printer.Id, 1);

        _printer.Print = FakeBambu.Report("RUNNING", 99.5, trayNow: 1, name: "a");
        await SyncAsync(printer.Id);
        _printer.Print = FakeBambu.Report("RUNNING", 1, trayNow: 1, name: "b");
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);
        // Dieselbe Datei noch einmal: Fortschritt fällt
        _printer.Print = FakeBambu.Report("RUNNING", 100, trayNow: 1, name: "b");
        await SyncAsync(printer.Id);
        _printer.Print = FakeBambu.Report("PREPARE", 0, trayNow: 1, name: "b");
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(1000 - 20, await RemainingAsync(black.Id), 2);
        Assert.Equal(["a", "b"], (await ConsumptionAsync(printer.Id)).Select(c => c.JobName).Order());
    }

    [Fact]
    public async Task IgnoresAPrintThatEndedBeforeThePrinterWasAdded()
    {
        _printer.Print = FakeBambu.Report("FINISH", 100, taskId: "4711");
        var printer = await CreatePrinterAsync();
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);
        Assert.Empty(_printer.Downloads);
    }

    [Fact]
    public async Task NeedsAnAccessCode()
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("P1S", PrinterType.Bambu, "http://192.168.2.170", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PassesASerialNumberGivenWithTheAccessCode()
    {
        var printer = await CreatePrinterAsync("01P00A123456789:12345678");
        Assert.Equal(("12345678", "01P00A123456789"), _printer.Logins[^1]);
        Assert.Null(printer.LastSyncError);
    }

    [Fact]
    public void ReadsThePrintedPlate()
    {
        var file = FakeBambu.ThreeMf(
            FakeBambu.Plate(1, (1, "PLA", "FFFFFF", 5, 1.6)),
            FakeBambu.Plate(2, (1, "PLA", "FFFFFF", 0, 0), (2, "PLA", "1A2B3C", 9.5, 3.1)));

        var plate = BambuLan.ReadSliceInfo(file, BambuLan.PlateOf("/data/Metadata/plate_2.gcode"))!;
        Assert.Equal(new SlicedFilament(2, "PLA", "1A2B3C", 9.5, 3.1), plate[1]);
        Assert.Equal(5, BambuLan.ReadSliceInfo(file, null)![0].UsedGrams);
    }

    [Fact]
    public void LooksForThePrintFileOnTheSdCardAndInTheCache()
    {
        Assert.Equal(["/x.gcode.3mf", "/x.3mf", "/cache/x.gcode.3mf", "/cache/x.3mf"], BambuLan.FileCandidates("/data/Metadata/plate_1.gcode", "x"));
        Assert.Equal("/models/x.3mf", BambuLan.FileCandidates("/sdcard/models/x.3mf", "x")[0]);
    }

    /// <summary>Abfrage gegen einen MQTT-Broker mit TLS, der wie der P1S erst auf pushall alles meldet.</summary>
    [Fact]
    public async Task QueriesTheFullStateOverMqttAndReadsTheSerialFromTheCertificate()
    {
        const string serial = "01P00A123456789";
        var port = FreePort();
        using var key = RSA.Create(2048);
        using var cert = new CertificateRequest($"CN={serial}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        using var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithoutDefaultEndpoint()
            .WithEncryptedEndpoint()
            .WithEncryptedEndpointPort(port)
            .WithEncryptedEndpointBoundIPAddress(IPAddress.Loopback)
            .WithEncryptedEndpointBoundIPV6Address(IPAddress.None)
            .WithEncryptionCertificate(new PrinterCertificate(X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null)))
            .Build());
        server.ValidatingConnectionAsync += e =>
        {
            if (e.UserName != "bblp" || e.Password != "12345678") e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        };
        server.InterceptingPublishAsync += async e =>
        {
            if (e.ApplicationMessage.Topic != $"device/{serial}/request" || !e.ApplicationMessage.ConvertPayloadToString().Contains("pushall")) return;
            // Erst eine Änderung, dann der vollständige Zustand
            foreach (var payload in new[] { """{"print":{"mc_percent":41,"command":"push_status","msg":1}}""", $$"""{"print":{{FakeBambu.Report("RUNNING", 42, trayNow: 2)}}}""" })
                await server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
                    .WithTopic($"device/{serial}/report")
                    .WithPayload(payload)
                    .Build()) { SenderClientId = "printer" });
        };
        await server.StartAsync();

        var report = await new BambuLan().QueryAsync($"http://127.0.0.1:{port}", "12345678", null, CancellationToken.None);

        Assert.Equal(serial, report.Serial);
        Assert.Equal("RUNNING", report.Print.GetProperty("gcode_state").GetString());
        Assert.Equal(42, report.Print.GetProperty("mc_percent").GetDouble());
        Assert.Equal(2, BambuAdapter.ReadAms(report.Print).Now);

        var wrong = await Assert.ThrowsAsync<PrinterUnavailableException>(() =>
            new BambuLan().QueryAsync($"http://127.0.0.1:{port}", "falsch", null, CancellationToken.None));
        Assert.Contains("MQTT", wrong.Message);
        await server.StopAsync();
    }

    [Fact]
    public void NumbersAmsSlotsFirstAndTheExternalSpoolLast()
    {
        var print = JsonDocument.Parse(FakeBambu.Report("RUNNING", 1, trayNow: 254)).RootElement;
        var ams = BambuAdapter.ReadAms(print);
        Assert.Equal(1, ams.Units);
        Assert.Equal(BambuAdapter.ExternalTray, ams.Now);
        Assert.Equal(4, BambuAdapter.SlotOf(ams.Now!.Value, BambuAdapter.SlotCount(ams.Units)));
        Assert.False(ams.Trays.ContainsKey(3));
        Assert.Equal(new BambuAdapter.Tray("PETG", "FF0000", -1, 0), ams.Trays[2]);
    }

    private class PrinterCertificate(X509Certificate2 certificate) : MQTTnet.Certificates.ICertificateProvider
    {
        public X509Certificate2 GetCertificate() => certificate;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
