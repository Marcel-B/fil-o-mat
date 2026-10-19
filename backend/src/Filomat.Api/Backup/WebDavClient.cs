using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Filomat.Api.Backup;

/// <summary>Die zwei WebDAV-Aufrufe einer Sicherung: MKCOL für den Ordner, PUT für die Datei.</summary>
public sealed class WebDavClient(IHttpClientFactory clients, IOptions<BackupOptions> options)
{
    public const string HttpClientName = "webdav";

    /// <summary>Legt den Sicherungsordner an, falls er fehlt.</summary>
    public async Task EnsureFolderAsync(CancellationToken cancellationToken)
    {
        using var request = Request(new HttpMethod("MKCOL"), "");
        using var response = await Client().SendAsync(request, cancellationToken);
        // 405: gibt es schon
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MethodNotAllowed)
            throw new WebDavException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Nextcloud lehnt Benutzer oder App-Passwort ab (401)",
                HttpStatusCode.Conflict => "Der Ordner über dem Sicherungsordner fehlt in der Nextcloud (409)",
                var status => $"Sicherungsordner lässt sich nicht anlegen (HTTP {(int)status})",
            });
    }

    /// <summary>Lädt die Datei hoch und ersetzt, was unter <paramref name="name"/> liegt.</summary>
    public async Task PutAsync(string name, string file, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Put, name);
        await using var stream = File.OpenRead(file);
        // Ein Stream mit Länge ergibt Content-Length; mit chunked uploads kommt Nextcloud schlecht zurecht.
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await Client().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new WebDavException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Nextcloud lehnt Benutzer oder App-Passwort ab (401)",
                HttpStatusCode.InsufficientStorage => "Die Nextcloud ist voll (507)",
                HttpStatusCode.Locked => $"Die Nextcloud hält {name} gesperrt (423)",
                var status => $"{name} lässt sich nicht hochladen (HTTP {(int)status})",
            });
    }

    private HttpClient Client()
    {
        var client = clients.CreateClient(HttpClientName);
        client.Timeout = options.Value.Timeout;
        return client;
    }

    private HttpRequestMessage Request(HttpMethod method, string name)
    {
        var settings = options.Value;
        var baseUri = settings.BaseUri ?? throw new WebDavException("Keine Nextcloud eingerichtet");
        var request = new HttpRequestMessage(method, new Uri(baseUri, Uri.EscapeDataString(name)));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ResolvedUsername}:{settings.ResolvedPassword}")));
        return request;
    }
}

/// <summary>Der WebDAV-Server lehnt ab oder antwortet nicht; die Meldung sagt, was zu tun ist.</summary>
public sealed class WebDavException(string message) : Exception(message);
