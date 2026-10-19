using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Formatter;

namespace Filomat.Api.Printers;

/// <summary>Zugang zum MQTT-Broker eines Anycubic-Druckers, gilt nur bis zu dessen Neustart.</summary>
public record AnycubicCredentials(string Host, int Port, string Username, string Password, string DeviceId, string ModelId);

/// <summary>Was der Drucker auf eine Abfrage meldet.</summary>
/// <param name="Info"><c>data</c> der <c>info</c>-Meldung (Zustand, laufender Druck in <c>project</c>).</param>
/// <param name="Boxes"><c>data.multi_color_box</c> der ACE-Meldung oder <c>null</c> ohne ACE.</param>
public record AnycubicReport(JsonElement Info, JsonElement? Boxes);

/// <summary>Fragt einen Anycubic-Drucker im LAN-Modus ab.</summary>
public interface IAnycubicLan
{
    Task<AnycubicReport> QueryAsync(string baseUrl, CancellationToken cancellationToken);
}

/// <summary>
/// LAN-Modus der Anycubic Kobra X, S1, 3 und 4 (Einstellungen > Netzwerk > LAN-Modus am Drucker). Ablauf nach
/// github.com/Nino6689/anycubic-lan (docs/PROTOCOL.md): <c>GET :18910/info</c> liefert ein Token, eine signierte
/// Anfrage an <c>/ctrl</c> die AES-verschlüsselten Zugangsdaten für den MQTT-Broker des Druckers (TLS, Port 9883).
/// Der Drucker meldet seinen Zustand dort nur auf Nachfrage, daher pro Abgleich: verbinden, fragen, trennen.
/// </summary>
public class AnycubicLan(HttpClient http) : IAnycubicLan
{
    public const int DiscoveryPort = 18910;
    private const string TopicPrefix = "anycubic/anycubicCloud/v1";
    private static readonly TimeSpan InfoTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BoxTimeout = TimeSpan.FromSeconds(4);

    public async Task<AnycubicReport> QueryAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var credentials = await HandshakeAsync(baseUrl, cancellationToken);
        var info = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var boxes = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var client = new MqttClientFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += e =>
        {
            try
            {
                using var message = JsonDocument.Parse(e.ApplicationMessage.ConvertPayloadToString());
                var root = message.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    return Task.CompletedTask;
                switch (root.GetStringOrNull("type"))
                {
                    case "info":
                        info.TrySetResult(data.Clone());
                        break;
                    case "multiColorBox" when data.TryGetProperty("multi_color_box", out var list) && list.ValueKind == JsonValueKind.Array:
                        boxes.TrySetResult(list.Clone());
                        break;
                }
            }
            catch (JsonException)
            {
                // Der Kobra X schickt viele leere oder unvollständige Meldungen
            }
            return Task.CompletedTask;
        };

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(credentials.Host, credentials.Port)
            .WithCredentials(credentials.Username, credentials.Password)
            .WithClientId($"filomat-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}")
            .WithProtocolVersion(MqttProtocolVersion.V311)
            // Selbst signiertes Zertifikat ohne festen Namen; die Verbindung bleibt im LAN
            .WithTlsOptions(tls => tls.UseTls().WithCertificateValidationHandler(_ => true))
            .WithTimeout(TimeSpan.FromSeconds(15))
            .Build();

        try
        {
            await client.ConnectAsync(options, cancellationToken);
            await client.SubscribeAsync($"{TopicPrefix}/printer/public/{credentials.ModelId}/{credentials.DeviceId}/#", cancellationToken: cancellationToken);
            await PublishAsync(client, credentials, "info", "query", cancellationToken);
            await PublishAsync(client, credentials, "multiColorBox", "getInfo", cancellationToken);

            var reportedInfo = await info.Task.WaitAsync(InfoTimeout, cancellationToken);
            // Ohne ACE antwortet niemand auf getInfo
            JsonElement? reportedBoxes = null;
            try
            {
                reportedBoxes = await boxes.Task.WaitAsync(BoxTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
            }
            await client.DisconnectAsync(cancellationToken: CancellationToken.None);
            return new AnycubicReport(reportedInfo, reportedBoxes);
        }
        catch (TimeoutException)
        {
            throw new PrinterUnavailableException("Der Drucker antwortet nicht auf MQTT-Abfragen.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Verbindungsfehler, abgelehnte Zugangsdaten, TLS
            throw new PrinterUnavailableException($"MQTT-Verbindung zum Drucker fehlgeschlagen: {ex.Message}", ex);
        }
    }

    private static Task PublishAsync(IMqttClient client, AnycubicCredentials credentials, string type, string action, CancellationToken cancellationToken) =>
        client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic($"{TopicPrefix}/web/printer/{credentials.ModelId}/{credentials.DeviceId}/{type}")
            .WithPayload(JsonSerializer.Serialize(new { type, action, data = new { } }))
            .Build(), cancellationToken);

    /// <summary>Holt die Zugangsdaten für den Broker; sie wechseln mit jedem Neustart des Druckers.</summary>
    public async Task<AnycubicCredentials> HandshakeAsync(string baseUrl, CancellationToken cancellationToken)
    {
        // Antwortet ohne JSON-Content-Type
        using var discovery = await GetJsonAsync(new Uri(DiscoveryBase(baseUrl), "info"), HttpMethod.Get, cancellationToken);
        var root = discovery.RootElement;
        if (root.GetStringOrNull("ctrlType") == "cloud")
            throw new PrinterUnavailableException("Der LAN-Modus ist am Drucker aus (Einstellungen > Netzwerk > LAN-Modus).");

        var token = root.GetStringOrNull("token");
        var ctrlUrl = root.GetStringOrNull("ctrlInfoUrl");
        // Mal Zahl, mal Text
        var modelId = root.GetStringOrNull("modelId");
        if (token is not { Length: 32 } || ctrlUrl is null || string.IsNullOrEmpty(modelId))
            throw new PrinterUnavailableException("Der Drucker unterstützt den LAN-Modus nicht (erst ab Kobra 3).");

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = RandomText("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 6);
        var did = RandomText("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 32);
        var url = $"{ctrlUrl}?ts={ts}&nonce={nonce}&sign={Sign(token, ts, nonce)}&did={did}";

        using var answer = await GetJsonAsync(new Uri(url), HttpMethod.Post, cancellationToken);
        var code = answer.RootElement.GetDoubleOrNull("code");
        if (code != 200)
            throw new PrinterUnavailableException($"Der Drucker lehnt die Anmeldung ab: {answer.RootElement.GetStringOrNull("message") ?? code?.ToString()}");
        answer.RootElement.TryGetProperty("data", out var data);
        var cipher = data.GetStringOrNull("info");
        var iv = data.GetStringOrNull("token");
        if (cipher is null || iv is null) throw new JsonException("Antwort des Druckers ohne Zugangsdaten.");

        using var plain = JsonDocument.Parse(Decrypt(cipher, token[16..], iv));
        var broker = new Uri(plain.RootElement.GetStringOrNull("broker") ?? throw new JsonException("Zugangsdaten ohne Broker."));
        return new AnycubicCredentials(
            broker.Host,
            broker.IsDefaultPort || broker.Port <= 0 ? 9883 : broker.Port,
            plain.RootElement.GetStringOrNull("username") ?? throw new JsonException("Zugangsdaten ohne Benutzer."),
            plain.RootElement.GetStringOrNull("password") ?? throw new JsonException("Zugangsdaten ohne Passwort."),
            plain.RootElement.GetStringOrNull("deviceId") ?? throw new JsonException("Zugangsdaten ohne Geräte-ID."),
            modelId);
    }

    /// <summary>Adresse des Druckers; ohne Port der feste Port 18910.</summary>
    public static Uri DiscoveryBase(string baseUrl)
    {
        var uri = new Uri(baseUrl.Contains("://") ? baseUrl : $"http://{baseUrl}");
        return new UriBuilder(uri) { Port = uri.IsDefaultPort ? DiscoveryPort : uri.Port, Path = "/" }.Uri;
    }

    /// <summary><c>md5(md5(token[0:16]) + ts + nonce)</c>, kleingeschrieben.</summary>
    public static string Sign(string token, string ts, string nonce)
    {
        var keyed = Convert.ToHexStringLower(MD5.HashData(Encoding.ASCII.GetBytes(token[..16])));
        return Convert.ToHexStringLower(MD5.HashData(Encoding.ASCII.GetBytes(keyed + ts + nonce)));
    }

    /// <summary>AES-128-CBC mit PKCS#7; IV ist der Text auf 16 Bytes gekürzt oder mit Nullen aufgefüllt.</summary>
    public static string Decrypt(string base64, string key, string ivText)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes(key);
        return Encoding.UTF8.GetString(aes.DecryptCbc(Convert.FromBase64String(base64), Iv(ivText)));
    }

    public static byte[] Iv(string text)
    {
        var iv = new byte[16];
        var bytes = Encoding.ASCII.GetBytes(text);
        Array.Copy(bytes, iv, Math.Min(16, bytes.Length));
        return iv;
    }

    private static string RandomText(string alphabet, int length) =>
        string.Create(length, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });

    private async Task<JsonDocument> GetJsonAsync(Uri uri, HttpMethod method, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
