using Filomat.Api.Backup;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Filomat.Api.Endpoints;

public static class BackupEndpoints
{
    public static RouteGroupBuilder MapBackupEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/backup").WithTags("Sicherung");

        group.MapGet("/", (CloudBackup backup) => Translated(backup.Status))
            .WithSummary("Ob die Nextcloud-Sicherung eingerichtet ist, wie die letzte lief und wann die nächste kommt");

        group.MapPost("/", Results<Accepted<BackupStatus>, ProblemHttpResult> (CloudBackup backup) =>
            {
                if (backup.Start()) return TypedResults.Accepted("/api/backup", Translated(backup.Status));
                return backup.Status.Configured
                    ? TypedResults.Problem(L.T("Es läuft schon eine Sicherung"), statusCode: StatusCodes.Status409Conflict)
                    : TypedResults.Problem(L.T("Keine Nextcloud eingerichtet (NEXTCLOUD_WEBDAV_URL, NEXTCLOUD_USERNAME, NEXTCLOUD_APP_PASSWORD)"),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithSummary("Startet eine Sicherung jetzt");

        return api;
    }

    /// <summary>Die Fehlermeldung der letzten Sicherung entsteht deutsch im Hintergrund.</summary>
    private static BackupStatus Translated(BackupStatus status) =>
        status.Last is { Error: { } error } last ? status with { Last = last with { Error = L.T(error) } } : status;
}
