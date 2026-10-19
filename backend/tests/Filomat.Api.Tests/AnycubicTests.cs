using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;
using Filomat.Api.Printers;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Filomat.Api.Tests;

/// <summary>Meldungen wie in github.com/Nino6689/anycubic-lan/docs/PROTOCOL.md (Kobra S1 und Kobra X).</summary>
public class FakeAnycubic : IAnycubicLan
{
    public string Info { get; set; } = """{"state":"free","project":null,"last_project":null}""";
    public string? Boxes { get; set; }
    public List<string> Queried { get; } = [];

    public Task<AnycubicReport> QueryAsync(string baseUrl, CancellationToken cancellationToken)
    {
        Queried.Add(baseUrl);
        return Task.FromResult(new AnycubicReport(
            JsonDocument.Parse(Info).RootElement.Clone(),
            Boxes is null ? null : JsonDocument.Parse(Boxes).RootElement.Clone()));
    }

    public static string Project(long taskId, double usageMm, double progress, int status) => $$"""
        {"remain_time":42,"curr_layer":3,"total_layers":5,"supplies_usage":{{usageMm}},"print_time":7,"progress":{{progress}},
         "state":"printing","print_status":{{status}},"filename":".3mf_temp/benchy_PLA_0.2.gcode","pause":0,
         "task_id":{{taskId}},"localtask":"b92a60d8-44d0-4b2a-916f-e5f476a07143"}
        """;

    public static string Printing(string project) => $$"""{"state":"busy","project":{{project}},"last_project":null}""";

    public static string Idle(string? lastProject = null) => $$"""{"state":"free","project":null,"last_project":{{lastProject ?? "null"}}}""";

    /// <summary>Eine ACE mit vier Slots, <paramref name="loaded"/> fördert.</summary>
    public static string Ace(int loaded, string type = "PLA") => $$"""
        [{"id":0,"status":1,"model_id":40002,"auto_feed":0,"loaded_slot":{{loaded}},"temp":25,
          "slots":[{"index":0,"sku":"","type":"{{type}}","color":[255,255,255],"status":4,"edit_status":0},
                   {"index":1,"sku":"","type":"{{type}}","color":[0,0,0],"status":4,"edit_status":0},
                   {"index":2,"sku":"","type":"PETG","color":[255,0,0],"status":4,"edit_status":1},
                   {"index":3,"sku":"","type":"","color":[0,0,0],"status":0,"edit_status":2}]}]
        """;
}

public class AnycubicTests : IDisposable
{
    private readonly FakeAnycubic _printer = new();
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public AnycubicTests()
    {
        _factory = new ApiFactory { AnycubicLan = _printer };
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private async Task<PrinterResponse> CreatePrinterAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("Kobra X", PrinterType.Anycubic, "http://192.168.2.160", null, null));
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

    private static double Grams(double mm, string type = "PLA") => Math.Round(FilamentMath.GramsFromLength(mm, type), 2);

    [Fact]
    public async Task BooksTheFinalLengthOnTheSpoolInTheFeedingSlot()
    {
        _printer.Boxes = FakeAnycubic.Ace(loaded: 1);
        var printer = await CreatePrinterAsync();
        var black = await PlaceSpoolAsync("B", printer.Id, 1);

        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(614707220, 1000, 20, 1));
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);
        // Kobra X meldet mitten im Druck print_status 0
        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(614707220, 3000, 70, 0));
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);
        // Nach dem Druck verschwindet project, last_project hat die endgültige Länge
        _printer.Boxes = FakeAnycubic.Ace(loaded: -1);
        _printer.Info = FakeAnycubic.Idle(FakeAnycubic.Project(614707220, 4000, 100, 2));
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(1000 - Grams(4000), await RemainingAsync(black.Id), 2);
        var listed = await _client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{printer.Id}");
        Assert.Equal(4, listed!.Tools);
        Assert.Null(listed.LastSyncError);
        Assert.All(_printer.Queried, url => Assert.Equal("http://192.168.2.160", url));
    }

    [Fact]
    public async Task SplitsAMultiColourPrintBetweenTheSlotsThatFedIt()
    {
        _printer.Boxes = FakeAnycubic.Ace(loaded: 0);
        var printer = await CreatePrinterAsync();
        var white = await PlaceSpoolAsync("W", printer.Id, 0);
        var red = await PlaceSpoolAsync("R", printer.Id, 2, "PETG");

        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(7, 500, 10, 1));
        await SyncAsync(printer.Id);
        // Bis hierher förderte Slot 0, ab jetzt Slot 2
        _printer.Boxes = FakeAnycubic.Ace(loaded: 2);
        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(7, 2000, 50, 1));
        await SyncAsync(printer.Id);
        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(7, 2600, 100, 2));
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        // Die ersten 500 mm sah der Adapter erst beim ersten Abgleich, also Slot 0
        Assert.Equal(1000 - Grams(2000), await RemainingAsync(white.Id), 2);
        Assert.Equal(1000 - Grams(600, "PETG"), await RemainingAsync(red.Id), 2);
    }

    [Fact]
    public async Task BooksACancelledPrintWithoutAceOnTheOnlySpool()
    {
        var printer = await CreatePrinterAsync();
        var spool = await PlaceSpoolAsync("S", printer.Id, 0);

        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(9, 800, 30, 1));
        await SyncAsync(printer.Id);
        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(9, 900, 32, 3));
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(1000 - Grams(900), await RemainingAsync(spool.Id), 2);
        var consumption = Assert.Single((await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={printer.Id}"))!);
        Assert.Equal("cancelled", consumption.Status);
        Assert.Equal("benchy_PLA_0.2", consumption.JobName);
    }

    [Fact]
    public async Task FinishesThePreviousPrintWhenANewOneStarts()
    {
        _printer.Boxes = FakeAnycubic.Ace(loaded: 1);
        var printer = await CreatePrinterAsync();
        var black = await PlaceSpoolAsync("B", printer.Id, 1);

        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(1, 1500, 99.8, 1));
        await SyncAsync(printer.Id);
        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(2, 100, 1, 1));
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(1000 - Grams(1500), await RemainingAsync(black.Id), 2);
    }

    [Fact]
    public void ReadsTheFeedingSlotFromItsStatusWhenLoadedSlotIsUnset()
    {
        var boxes = JsonDocument.Parse("""
            [{"id":0,"loaded_slot":-1,"slots":[{"index":0,"type":"PLA","status":4}]},
             {"id":1,"loaded_slot":-1,"slots":[{"index":0,"type":"PLA","status":4},{"index":2,"type":"ASA","status":5}]}]
            """).RootElement;
        var ace = AnycubicAdapter.ReadAce(boxes);
        Assert.Equal(8, ace.SlotCount);
        Assert.Equal(6, ace.LoadedSlot);
        Assert.Equal("ASA", ace.Types[6]);
    }

    /// <summary>Marcels Kobra X (Firmware 2.0.2.2, gekürzt): eingebauter Vierer als Einheit -1, Slot 3 reicht zur externen ACE durch.</summary>
    private const string KobraXBoxes = """
        [{"id":-1,"loaded_slot":-1,"model_id":40002,"slots":[
           {"index":0,"type":"PLA+","color":[255,255,255],"status":5,"edit_status":1},
           {"index":1,"type":"PLA+","color":[101,88,177],"status":5,"edit_status":1},
           {"index":2,"type":"PLA Matte","color":[33,39,33],"status":4,"edit_status":2},
           {"index":3,"type":"PLA","color":[244,0,49],"status":5,"edit_status":3}]},
         {"id":0,"loaded_slot":-1,"model_id":40002,"humidity":45,"slots":[
           {"index":0,"type":"PLA+","status":4,"edit_status":2},
           {"index":1,"type":"PLA+","status":4,"edit_status":2},
           {"index":2,"type":"PLA+","status":4,"edit_status":2},
           {"index":3,"type":"PLA+","status":4,"edit_status":2}]}]
        """;

    [Fact]
    public void NumbersTheKobraXBuiltInSlotsFirstAndSkipsThePassThrough()
    {
        var ace = AnycubicAdapter.ReadAce(JsonDocument.Parse(KobraXBoxes).RootElement);
        Assert.Equal(8, ace.SlotCount);
        // Mehrere Slots im Kopf eingezogen: daraus lässt sich der fördernde nicht ablesen
        Assert.Null(ace.LoadedSlot);
        Assert.Equal("PLA Matte", ace.Types[2]);
        Assert.False(ace.Types.ContainsKey(3));
        Assert.Equal("PLA+", ace.Types[7]);

        var feeding = KobraXBoxes.Replace("\"id\":0,\"loaded_slot\":-1", "\"id\":0,\"loaded_slot\":2");
        Assert.Equal(6, AnycubicAdapter.ReadAce(JsonDocument.Parse(feeding).RootElement).LoadedSlot);
        var internalSlot = KobraXBoxes.Replace("\"id\":-1,\"loaded_slot\":-1", "\"id\":-1,\"loaded_slot\":1");
        Assert.Equal(1, AnycubicAdapter.ReadAce(JsonDocument.Parse(internalSlot).RootElement).LoadedSlot);
    }

    [Fact]
    public async Task FollowsAPrintThatStartsWithoutATaskId()
    {
        _printer.Boxes = KobraXBoxes.Replace("\"id\":-1,\"loaded_slot\":-1", "\"id\":-1,\"loaded_slot\":0");
        var printer = await CreatePrinterAsync();
        var white = await PlaceSpoolAsync("W", printer.Id, 0);

        // Kobra X beim Aufheizen: task_id -1, noch nichts gefördert
        _printer.Info = FakeAnycubic.Printing("""{"task_id":-1,"state":"nozzle_heating","progress":0,"supplies_usage":0}""");
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);
        _printer.Info = FakeAnycubic.Printing("""{"task_id":-1,"state":"printing","progress":5,"supplies_usage":300,"filename":"cube.gcode"}""");
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);
        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(555, 900, 50, 0));
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);
        _printer.Info = FakeAnycubic.Idle("""{"task_id":555,"state":"finished","progress":1,"supplies_usage":1500}""");
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(1000 - Grams(1500), await RemainingAsync(white.Id), 2);
        var consumption = Assert.Single((await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={printer.Id}"))!);
        Assert.Equal("555", consumption.JobId);
        Assert.Equal("cube", consumption.JobName);
    }

    [Fact]
    public async Task BooksPrintsThatNeverGetATaskIdSeparately()
    {
        var printer = await CreatePrinterAsync();
        var spool = await PlaceSpoolAsync("S", printer.Id, 0);

        foreach (var usage in new[] { 400, 700 })
        {
            _printer.Info = FakeAnycubic.Printing($$"""{"task_id":-1,"state":"printing","progress":50,"supplies_usage":{{usage / 2}}}""");
            await SyncAsync(printer.Id);
            _printer.Info = FakeAnycubic.Idle($$"""{"task_id":-1,"state":"finished","progress":1,"supplies_usage":{{usage}}}""");
            Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);
            await Task.Delay(1100);
        }

        Assert.Equal(1000 - Grams(400) - Grams(700), await RemainingAsync(spool.Id), 1);
    }

    [Fact]
    public async Task UsesTheStateOfTheKobraXLastProject()
    {
        _printer.Boxes = KobraXBoxes.Replace("\"id\":0,\"loaded_slot\":-1", "\"id\":0,\"loaded_slot\":1");
        var printer = await CreatePrinterAsync();
        Assert.Equal(8, (await _client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{printer.Id}"))!.Tools);
        var blue = await PlaceSpoolAsync("BL", printer.Id, 5);

        _printer.Info = FakeAnycubic.Printing(FakeAnycubic.Project(129923691, 1200, 40, 0));
        await SyncAsync(printer.Id);
        // Wie vom Kobra X gemeldet: last_project ohne print_status, Fortschritt 1
        _printer.Info = FakeAnycubic.Idle("""
            {"curr_layer":24,"filename":"1011-1729-Eurorack_RevB_Parts_DovetailConnector_4mm_plate(01)_PLA_0.2_52m13s.gcode",
             "print_time":54,"progress":1,"remain_time":0,"state":"finished","supplies_usage":2601.63301000005,"task_id":129923691,"total_layers":24}
            """);
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(1000 - Grams(2601.63301), await RemainingAsync(blue.Id), 2);
        var consumption = Assert.Single((await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={printer.Id}"))!);
        Assert.Equal("completed", consumption.Status);
    }
}

/// <summary>Anmeldung am Drucker: Token, Signatur, entschlüsselte Zugangsdaten.</summary>
public class AnycubicHandshakeTests
{
    private const string Token = "0123456789abcdeffedcba9876543210";

    private class FakeDiscovery(string discovery, Func<HttpRequestMessage, string> ctrl) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var body = request.RequestUri!.AbsolutePath == "/info" ? discovery : ctrl(request);
            // Der Drucker schickt keinen JSON-Content-Type
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") });
        }
    }

    [Fact]
    public void SignsLikeThePrinterExpects()
    {
        // Mit Python berechnet: md5(md5(token[:16]).hexdigest() + ts + nonce)
        Assert.Equal("4f4d5e5761a00c14bde8f5f3fd43e845", AnycubicLan.Sign(Token, "1754640000000", "aB3xY9"));
    }

    [Theory]
    [InlineData("http://192.168.2.160", "http://192.168.2.160:18910/")]
    [InlineData("192.168.2.160", "http://192.168.2.160:18910/")]
    [InlineData("http://kobra.local:8080/x", "http://kobra.local:8080/")]
    public void UsesTheDiscoveryPortUnlessOneIsGiven(string baseUrl, string expected) =>
        Assert.Equal(expected, AnycubicLan.DiscoveryBase(baseUrl).ToString());

    [Fact]
    public async Task DecryptsTheBrokerCredentials()
    {
        const string iv = "short-iv";
        var plain = """{"broker":"mqtts://192.168.2.160:9883","username":"user","password":"pass","deviceId":"372d94454cf5d746d07a8100df8674aa"}""";
        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes(Token[16..]);
        var cipher = Convert.ToBase64String(aes.EncryptCbc(Encoding.UTF8.GetBytes(plain), AnycubicLan.Iv(iv)));

        var handler = new FakeDiscovery(
            $$"""{"ctrlType":"lan","token":"{{Token}}","ctrlInfoUrl":"http://192.168.2.160:18910/ctrl","modelId":"20030","modelName":"Anycubic Kobra X"}""",
            request =>
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal(AnycubicLan.Sign(Token, query["ts"]!, query["nonce"]!), query["sign"]);
                Assert.Equal(32, query["did"]!.Length);
                return $$$"""{"code":200,"data":{"info":"{{{cipher}}}","token":"{{{iv}}}"}}""";
            });

        var credentials = await new AnycubicLan(new HttpClient(handler)).HandshakeAsync("http://192.168.2.160", CancellationToken.None);

        Assert.Equal(new AnycubicCredentials("192.168.2.160", 9883, "user", "pass", "372d94454cf5d746d07a8100df8674aa", "20030"), credentials);
        Assert.Equal("http://192.168.2.160:18910/info", handler.Requests[0].ToString());
    }

    /// <summary>Handshake und Abfrage gegen einen MQTT-Broker mit TLS, der wie der Drucker nur auf Nachfrage antwortet.</summary>
    [Fact]
    public async Task QueriesStateAndAceOverMqtt()
    {
        const string device = "372d94454cf5d746d07a8100df8674aa";
        var port = FreePort();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=printer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

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
            if (e.UserName != "user" || e.Password != "pass") e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        };
        server.InterceptingPublishAsync += async e =>
        {
            var topic = e.ApplicationMessage.Topic;
            if (!topic.StartsWith($"anycubic/anycubicCloud/v1/web/printer/20030/{device}/")) return;
            var type = topic[(topic.LastIndexOf('/') + 1)..];
            var answer = type switch
            {
                "info" => $$$"""{"type":"info","action":"report","state":"done","code":200,"data":{"state":"busy","project":{{{FakeAnycubic.Project(5, 1234, 40, 1)}}}}}""",
                "multiColorBox" => $$$"""{"type":"multiColorBox","action":"getInfo","state":"success","code":200,"data":{"multi_color_box":{{{FakeAnycubic.Ace(2)}}}}}""",
                _ => null,
            };
            if (answer is null) return;
            // Leere Meldungen wie vom Kobra X dazwischen
            foreach (var payload in new[] { """{"type":""}""", """{"msgid":""}""", answer })
                await server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
                    .WithTopic($"anycubic/anycubicCloud/v1/printer/public/20030/{device}/{type}/report")
                    .WithPayload(payload)
                    .Build()) { SenderClientId = "printer" });
        };
        await server.StartAsync();

        const string iv = "0123456789abcdefXYZ";
        var plain = $$"""{"broker":"mqtts://127.0.0.1:{{port}}","username":"user","password":"pass","deviceId":"{{device}}"}""";
        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes(Token[16..]);
        var cipher = Convert.ToBase64String(aes.EncryptCbc(Encoding.UTF8.GetBytes(plain), AnycubicLan.Iv(iv)));
        var handler = new FakeDiscovery(
            $$"""{"ctrlType":"lan","token":"{{Token}}","ctrlInfoUrl":"http://127.0.0.1:18910/ctrl","modelId":20030}""",
            _ => $$$"""{"code":200,"data":{"info":"{{{cipher}}}","token":"{{{iv}}}"}}""");

        var report = await new AnycubicLan(new HttpClient(handler)).QueryAsync("http://127.0.0.1", CancellationToken.None);

        Assert.Equal(1234, report.Info.GetProperty("project").GetProperty("supplies_usage").GetDouble());
        Assert.Equal(2, AnycubicAdapter.ReadAce(report.Boxes).LoadedSlot);
        await server.StopAsync();
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

    [Fact]
    public async Task ReportsWhenLanModeIsOff()
    {
        var handler = new FakeDiscovery("""{"ctrlType":"cloud","token":"x","modelId":20030}""", _ => throw new InvalidOperationException());
        var error = await Assert.ThrowsAsync<PrinterUnavailableException>(() =>
            new AnycubicLan(new HttpClient(handler)).HandshakeAsync("http://192.168.2.160", CancellationToken.None));
        Assert.Contains("LAN-Modus", error.Message);
    }
}
