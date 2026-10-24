using Filomat.Api.Notifications;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Filomat.Api.Endpoints;

/// <param name="Url">Adresse des ntfy-Themas; leer schaltet die Meldungen aus der App aus.</param>
/// <param name="Token"><c>null</c> lässt den gespeicherten Zugangstoken unverändert, ein leerer Text löscht ihn.</param>
public record NotificationSettingsRequest(string? Url, string? Token);

/// <param name="FromApp"><c>false</c>, wenn die Adresse aus der Server-Konfiguration (<c>Ntfy__Url</c>) kommt.</param>
public record NotificationSettingsResponse(string? Url, bool HasToken, bool FromApp, bool Configured)
{
    public static NotificationSettingsResponse From(NtfySettings s) => new(s.Url, !string.IsNullOrEmpty(s.Token), s.FromApp, s.Configured);
}

public static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/settings/notifications").WithTags("Meldungen");

        group.MapGet("/", async (Notifier notifier, CancellationToken cancellationToken) =>
            NotificationSettingsResponse.From(await notifier.GetSettingsAsync(cancellationToken)));

        group.MapPut("/", async Task<Results<Ok<NotificationSettingsResponse>, ValidationProblem>> (
            NotificationSettingsRequest request, Notifier notifier, CancellationToken cancellationToken) =>
        {
            var url = request.Url?.Trim();
            if (!string.IsNullOrEmpty(url) && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["Adresse muss mit http:// oder https:// beginnen, z. B. https://ntfy.sh/mein-thema."] });
            await notifier.SaveSettingsAsync(url, request.Token, cancellationToken);
            return TypedResults.Ok(NotificationSettingsResponse.From(await notifier.GetSettingsAsync(cancellationToken)));
        });

        group.MapPost("/test", async Task<Results<NoContent, BadRequest<string>>> (Notifier notifier, CancellationToken cancellationToken) =>
        {
            try
            {
                return await notifier.SendAsync("fil-O-mat", "Testmeldung: Meldungen kommen an.", "tada", throwOnError: true, cancellationToken: cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.BadRequest("Keine ntfy-Adresse eingestellt.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return TypedResults.BadRequest($"ntfy antwortet nicht wie erwartet: {ex.Message}");
            }
        }).WithSummary("Testmeldung schicken");

        return api;
    }
}
