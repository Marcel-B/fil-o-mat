using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;
using Filomat.Api.Monitoring;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Filomat.Api.Tests;

/// <summary>API mit eingeschaltetem /metrics.</summary>
public class MetricsApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Monitoring:Metrics", "true");
    }
}

/// <summary>Die Zähler sind prozessweit; andere Testklassen gleichen auch Moonraker-Drucker ab und dürfen nicht dazwischen zählen.</summary>
[CollectionDefinition(nameof(MetricsCollection), DisableParallelization = true)]
public class MetricsCollection;

[Collection(nameof(MetricsCollection))]
public class MetricsTests : IDisposable
{
    private readonly FakeMoonraker _moonraker = new();
    private readonly MetricsApiFactory _factory;
    private readonly HttpClient _client;

    public MetricsTests()
    {
        _factory = new MetricsApiFactory { PrinterHandler = _moonraker, ApiKey = "geheim" };
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "geheim");
    }

    public void Dispose() => _factory.Dispose();

    private async Task<PrinterResponse> CreatePrinterAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest(name, PrinterType.Moonraker, "http://voron.local:7125", null, null));
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>())!;
    }

    /// <summary>Wert einer Zeile in /metrics, 0 wenn sie fehlt (Zähler sind prozessweit, Tests vergleichen Differenzen).</summary>
    private async Task<double> ValueAsync(string series)
    {
        var text = await _client.GetStringAsync("/metrics");
        var line = text.Split('\n').FirstOrDefault(l => l.StartsWith(series + " ", StringComparison.Ordinal));
        return line is null ? 0 : double.Parse(line[(series.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task MetricsAnswerWithoutApiKeyAndNameEverySeries()
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        foreach (var name in new[]
                 {
                     "filomat_printer_syncs_total", "filomat_printer_sync_duration_seconds", "filomat_printers", "filomat_print_jobs_total",
                     "filomat_filament_used_grams_total", "filomat_spools", "filomat_containers", "filomat_items",
                     "filomat_notifications_total", "filomat_backup_runs_total", "filomat_api_key_rejected_total", "http_requests_received_total",
                 })
            Assert.Contains($"# TYPE {name} ", text);
    }

    [Fact]
    public async Task SyncsCountByResultAndPrinterNamesStayOut()
    {
        var printer = await CreatePrinterAsync("Werkstatt-Voron-Geheimname");
        _moonraker.Body = FakeMoonraker.History(("000001", "completed", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), 5000, 10000, 30));
        var ok = await ValueAsync("filomat_printer_syncs_total{type=\"moonraker\",result=\"ok\"}");
        var jobs = await ValueAsync("filomat_print_jobs_total{type=\"moonraker\"}");
        var grams = await ValueAsync("filomat_filament_used_grams_total{type=\"moonraker\"}");

        await _client.PostAsync($"/api/printers/{printer.Id}/sync", null);

        Assert.Equal(ok + 1, await ValueAsync("filomat_printer_syncs_total{type=\"moonraker\",result=\"ok\"}"));
        Assert.Equal(jobs + 1, await ValueAsync("filomat_print_jobs_total{type=\"moonraker\"}"));
        Assert.Equal(grams + 15, await ValueAsync("filomat_filament_used_grams_total{type=\"moonraker\"}"), 3);

        // 401 vom Drucker: PrinterUnavailableException im Adapter, der Drucker gilt als unerreichbar
        _moonraker.Status = HttpStatusCode.Unauthorized;
        var unreachable = await ValueAsync("filomat_printer_syncs_total{type=\"moonraker\",result=\"unreachable\"}");
        await _client.PostAsync($"/api/printers/{printer.Id}/sync", null);
        Assert.Equal(unreachable + 1, await ValueAsync("filomat_printer_syncs_total{type=\"moonraker\",result=\"unreachable\"}"));
        Assert.True(await ValueAsync("filomat_printers{type=\"moonraker\",state=\"unreachable\"}") >= 1);

        var text = await _client.GetStringAsync("/metrics");
        Assert.DoesNotContain("Geheimname", text);
        Assert.DoesNotContain(printer.Id, text);
    }

    [Fact]
    public async Task WrongApiKeyIsCounted()
    {
        var before = await ValueAsync("filomat_api_key_rejected_total");
        var stranger = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/containers")).StatusCode);
        Assert.True(await ValueAsync("filomat_api_key_rejected_total") >= before + 1);
    }
}

public class MetricsOffTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task WithoutSettingMetricsAre404()
    {
        var response = await _factory.CreateClient().GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public class LokiTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(Uri? Url, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public void OptionsAreOffWithoutSettings()
    {
        var options = MonitoringOptions.Load(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Assert.False(options.Metrics);
        Assert.Null(options.LokiPushUrl);
    }

    [Theory]
    [InlineData("http://monitoring:3100/")]
    [InlineData("http://monitoring:3100/loki/api/v1/push")]
    public void PushPathIsAddedOnce(string url) =>
        Assert.Equal("http://monitoring:3100/loki/api/v1/push", new MonitoringOptions { LokiUrl = url }.LokiPushUrl);

    [Theory]
    [InlineData("Filomat.Api.Printers.ConsumptionSync", "printers")]
    [InlineData("Filomat.Api.Backup.CloudBackup", "backup")]
    [InlineData("Filomat.Api.Notifications.Notifier", "notifications")]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics", "server")]
    public void SourceComesFromTheNamespace(string category, string source) =>
        Assert.Equal(source, LokiLoggerProvider.SourceOf(category));

    [Fact]
    public async Task LinesGoOutWithFourLabels()
    {
        var handler = new CapturingHandler();
        using var shipper = new LokiShipper(new MonitoringOptions { LokiUrl = "http://monitoring:3100", LokiHost = "proxmox" }, handler);
        var logger = new LokiLoggerProvider(shipper).CreateLogger("Filomat.Api.Printers.ConsumptionSync");
        logger.LogWarning("Abgleich fehlgeschlagen");
        logger.LogInformation("fertig");

        Assert.True(await shipper.FlushAsync(CancellationToken.None));

        var (url, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://monitoring:3100/loki/api/v1/push", url!.ToString());
        using var json = JsonDocument.Parse(body);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToList();
        Assert.Equal(2, streams.Count);
        var warning = streams.Single(s => s.GetProperty("stream").GetProperty("level").GetString() == "warning");
        var labels = warning.GetProperty("stream");
        Assert.Equal("filomat", labels.GetProperty("app").GetString());
        Assert.Equal("proxmox", labels.GetProperty("host").GetString());
        Assert.Equal("printers", labels.GetProperty("source").GetString());
        Assert.Equal(4, labels.EnumerateObject().Count());
        Assert.Equal("[Filomat.Api.Printers.ConsumptionSync] Abgleich fehlgeschlagen", warning.GetProperty("values")[0][1].GetString());
    }
}
