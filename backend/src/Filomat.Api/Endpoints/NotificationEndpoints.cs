using System.Globalization;
using Filomat.Api.Notifications;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Filomat.Api.Endpoints;

/// <param name="Server">Adresse des Onkel-Bummel-Servers; leer schaltet die Meldungen aus der App aus (es gilt die Server-Konfiguration).</param>
/// <param name="Topic">Topic, an das die Meldungen gehen (<c>a–z</c>, <c>0–9</c>, <c>_</c>, <c>-</c>, höchstens 64 Zeichen).</param>
/// <param name="Token">Publish-Token (<c>ob_…</c>); <c>null</c> lässt den gespeicherten unverändert.</param>
/// <param name="Language">Sprache der Meldungen (<c>de</c>, <c>en</c>); ohne Angabe die Sprache der App, die speichert.</param>
public record NotificationSettingsRequest(string? Server, string? Topic, string? Token, string? Language = null);

/// <param name="FromApp"><c>false</c>, wenn das Ziel aus der Server-Konfiguration (<c>Notifications__Server</c>) kommt.</param>
public record NotificationSettingsResponse(string? Server, string? Topic, bool HasToken, bool FromApp, bool Configured, string Language)
{
    public static NotificationSettingsResponse From(NotificationSettings s) =>
        new(s.Server, s.Topic, !string.IsNullOrEmpty(s.Token), s.FromApp, s.Configured, s.Language);
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
            var server = request.Server?.Trim();
            var topic = request.Topic?.Trim();
            var token = request.Token?.Trim();
            var errors = new Dictionary<string, string[]>();
            if (!string.IsNullOrEmpty(server))
            {
                if (!Notifier.IsServerUrl(server))
                    errors["server"] = [L.T("Adresse muss mit http:// oder https:// beginnen, z. B. https://onkel-bummel.example.ts.net.")];
                if (!Notifier.IsTopic(topic))
                    errors["topic"] = [L.T("Topic: nur Kleinbuchstaben, Ziffern, _ und -, höchstens 64 Zeichen, z. B. filomat.")];
                // Ohne neuen Token muss schon einer gespeichert sein
                if (!Notifier.IsToken(token is null ? await notifier.StoredTokenAsync(cancellationToken) : token))
                    errors["token"] = [L.T("Token muss mit ob_ beginnen (in Onkel Bummel unter Verwaltung → Publish-Tokens).")];
            }
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var language = request.Language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            await notifier.SaveSettingsAsync(server, topic, token, language, cancellationToken);
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
                    : TypedResults.BadRequest(L.T("Kein Ziel für Meldungen eingestellt (Server, Topic und Token)."));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return TypedResults.BadRequest(L.F("Onkel Bummel antwortet nicht wie erwartet: {0}", ex.Message));
            }
        }).WithSummary("Testmeldung schicken");

        return api;
    }
}
