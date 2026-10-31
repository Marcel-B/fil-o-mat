using System.Globalization;
using Filomat.Api.Notifications;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Filomat.Api.Endpoints;

/// <param name="Url">Adresse des ntfy-Themas; leer schaltet die Meldungen aus der App aus.</param>
/// <param name="Token"><c>null</c> lässt den gespeicherten Zugangstoken unverändert, ein leerer Text löscht ihn.</param>
/// <param name="Language">Sprache der Meldungen (<c>de</c>, <c>en</c>); ohne Angabe die Sprache der App, die speichert.</param>
public record NotificationSettingsRequest(string? Url, string? Token, string? Language = null);

/// <param name="FromApp"><c>false</c>, wenn die Adresse aus der Server-Konfiguration (<c>Ntfy__Url</c>) kommt.</param>
public record NotificationSettingsResponse(string? Url, bool HasToken, bool FromApp, bool Configured, string Language)
{
    public static NotificationSettingsResponse From(NtfySettings s) => new(s.Url, !string.IsNullOrEmpty(s.Token), s.FromApp, s.Configured, s.Language);
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
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["url"] = [L.T("Adresse muss mit http:// oder https:// beginnen, z. B. https://ntfy.sh/mein-thema.")] });
            var language = request.Language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            await notifier.SaveSettingsAsync(url, request.Token, language, cancellationToken);
            return TypedResults.Ok(NotificationSettingsResponse.From(await notifier.GetSettingsAsync(cancellationToken)));
        });

        group.MapPost("/test", async Task<Results<NoContent, BadRequest<string>>> (Notifier notifier, CancellationToken cancellationToken) =>
        {
            try
            {
                string message;
                using (L.Use(await notifier.LanguageAsync(cancellationToken)))
                    message = L.T("Testmeldung: Meldungen kommen an.");
                return await notifier.SendAsync("fil-O-mat", message, "tada", throwOnError: true, cancellationToken: cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.BadRequest(L.T("Keine ntfy-Adresse eingestellt."));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return TypedResults.BadRequest(L.F("ntfy antwortet nicht wie erwartet: {0}", ex.Message));
            }
        }).WithSummary("Testmeldung schicken");

        return api;
    }
}
