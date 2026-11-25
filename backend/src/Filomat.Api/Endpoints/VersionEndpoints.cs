namespace Filomat.Api.Endpoints;

/// <summary>
/// API-Stufe des Backends, damit die Apps erkennen, ob sie zum Backend passen.
/// <para><see cref="Level"/> steigt um eins, sobald die API etwas Neues kann, auf das sich eine App verlässt.</para>
/// <para><see cref="MinAppLevel"/> steigt nur, wenn ältere Apps nicht mehr funktionieren (Feld oder Endpunkt entfernt oder geändert).</para>
/// </summary>
public static class ApiVersion
{
    /// <summary>
    /// 1: <c>/api/version</c> eingeführt.
    /// 2: Meldungen über Onkel Bummel, <c>/api/settings/notifications</c> mit <c>server</c>, <c>topic</c>, <c>token</c> statt <c>url</c>.
    /// </summary>
    public const int Level = 2;

    /// <summary>2: Apps der Stufe 1 schicken für Meldungen noch <c>url</c> (ntfy).</summary>
    public const int MinAppLevel = 2;
}

/// <param name="ApiLevel">Stufe dieses Backends.</param>
/// <param name="MinAppLevel">Ältere Apps (mit kleinerer Stufe) passen nicht mehr.</param>
/// <param name="Commit">Laufender Commit laut Updater, sonst <c>null</c>.</param>
public record VersionResponse(int ApiLevel, int MinAppLevel, string? Commit);

public static class VersionEndpoints
{
    public static RouteGroupBuilder MapVersionEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/version", (IConfiguration configuration) =>
                new VersionResponse(ApiVersion.Level, ApiVersion.MinAppLevel, UpdateEndpoints.ReadStatus(configuration).CurrentVersion))
            .WithTags("Aktualisierung")
            .WithSummary("API-Stufe, damit die Apps prüfen können, ob sie zum Backend passen");
        return api;
    }
}
