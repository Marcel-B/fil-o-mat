using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Filomat.Api.Endpoints;

/// <summary>Ein neuer Commit auf dem Branch, den das Backend baut. Datum in Millisekunden seit 1970.</summary>
public record UpdateCommit(string Sha, string Subject, long Date);

/// <summary>
/// Stand der Aktualisierung. <see cref="Supported"/> ist <c>false</c>, wenn auf dem Server kein Updater eingerichtet ist.
/// <see cref="State"/>: <c>idle</c>, <c>requested</c> (Auftrag liegt bereit), <c>running</c> oder <c>failed</c>.
/// </summary>
public record UpdateStatus(
    bool Supported,
    string? Branch,
    string? CurrentVersion,
    long? CurrentDate,
    string? LatestVersion,
    bool UpdateAvailable,
    List<UpdateCommit> Commits,
    long? CheckedAt,
    string State,
    string? Message)
{
    public static readonly UpdateStatus Unsupported = new(false, null, null, null, null, false, [], null, "idle", null);
}

/// <summary>
/// Das Backend aktualisiert sich nicht selbst: auf dem Host (LXC) prüft <c>deploy/updater/filomat-update.sh</c>
/// regelmäßig den Branch und schreibt das Ergebnis nach <c>status.json</c>. Die API liest diese Datei und legt
/// für einen Auftrag die Datei <c>request</c> an, auf die der Updater per systemd wartet.
/// </summary>
public static class UpdateEndpoints
{
    private const string StatusFile = "status.json";
    private const string RequestFile = "request";

    public static RouteGroupBuilder MapUpdateEndpoints(this RouteGroupBuilder api)
    {
        var update = api.MapGroup("/update").WithTags("Aktualisierung");

        update.MapGet("/", (IConfiguration configuration) => ReadStatus(configuration))
            .WithSummary("Läuft die neueste Version? Liste der neuen Commits");

        update.MapPost("/check", Results<Accepted<UpdateStatus>, ProblemHttpResult> (IConfiguration configuration) =>
        {
            if (UpdateDirectory(configuration) is not { } dir || !File.Exists(Path.Combine(dir, StatusFile)))
                return NotSupported();
            // Ein wartendes Update nicht durch eine bloße Prüfung ersetzen
            if (!File.Exists(Path.Combine(dir, RequestFile)))
                File.WriteAllText(Path.Combine(dir, RequestFile), "check\n");
            return TypedResults.Accepted("/api/update", ReadStatus(configuration));
        }).WithSummary("Sofort auf neue Commits prüfen lassen");

        update.MapPost("/", Results<Accepted<UpdateStatus>, ProblemHttpResult> (IConfiguration configuration) =>
        {
            if (UpdateDirectory(configuration) is not { } dir || !File.Exists(Path.Combine(dir, StatusFile)))
                return NotSupported();
            var status = ReadStatus(configuration);
            if (status.State == "running")
                return TypedResults.Problem("Die Aktualisierung läuft bereits.", statusCode: StatusCodes.Status409Conflict);
            if (!status.UpdateAvailable)
                return TypedResults.Problem("Es gibt keine neue Version.", statusCode: StatusCodes.Status409Conflict);
            File.WriteAllText(Path.Combine(dir, RequestFile), "apply\n");
            return TypedResults.Accepted("/api/update", ReadStatus(configuration));
        }).WithSummary("Neue Version einspielen (das Backend startet dabei neu)");

        return api;
    }

    private static ProblemHttpResult NotSupported() => TypedResults.Problem(
        "Auf dem Server ist kein Updater eingerichtet (deploy/updater/install.sh).",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string? UpdateDirectory(IConfiguration configuration) =>
        configuration["Filomat:UpdateDirectory"] is { Length: > 0 } dir && Directory.Exists(dir) ? dir : null;

    public static UpdateStatus ReadStatus(IConfiguration configuration)
    {
        if (UpdateDirectory(configuration) is not { } dir) return UpdateStatus.Unsupported;

        HostStatus? host;
        try
        {
            host = JsonSerializer.Deserialize<HostStatus>(File.ReadAllText(Path.Combine(dir, StatusFile)));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return UpdateStatus.Unsupported;
        }
        if (host is null) return UpdateStatus.Unsupported;

        var commits = (host.Commits ?? []).Select(c => new UpdateCommit(c.Sha, c.Subject, c.Date * 1000)).ToList();
        var request = Path.Combine(dir, RequestFile);
        var requested = File.Exists(request) && File.ReadAllText(request).Trim() == "apply";
        var state = host.State == "running" ? "running" : requested ? "requested" : host.State ?? "idle";
        return new UpdateStatus(
            true,
            host.Branch,
            host.Current,
            host.CurrentDate * 1000,
            host.Latest,
            commits.Count > 0,
            commits,
            host.CheckedAt * 1000,
            state,
            host.Message);
    }

    /// <summary>Format von <c>status.json</c>, wie es <c>filomat-update.sh</c> schreibt (Zeiten in Sekunden).</summary>
    private record HostStatus(
        [property: JsonPropertyName("branch")] string? Branch,
        [property: JsonPropertyName("current")] string? Current,
        [property: JsonPropertyName("currentDate")] long? CurrentDate,
        [property: JsonPropertyName("latest")] string? Latest,
        [property: JsonPropertyName("commits")] List<HostCommit>? Commits,
        [property: JsonPropertyName("checkedAt")] long? CheckedAt,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("message")] string? Message);

    private record HostCommit(
        [property: JsonPropertyName("sha")] string Sha,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("date")] long Date);
}
