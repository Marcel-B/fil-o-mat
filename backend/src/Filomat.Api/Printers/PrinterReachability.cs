using System.Collections.Concurrent;
using System.Net.Sockets;
using Filomat.Api.Domain;
using MQTTnet.Adapter;
using MQTTnet.Exceptions;

namespace Filomat.Api.Printers;

/// <summary>
/// Merkt sich pro Drucker, ob er erreichbar ist, und entscheidet, was davon ins Log kommt. Ein ausgeschalteter Drucker
/// ist ein normaler Zustand: Der Wechsel kommt einmal als Info, danach nur Debug. Eine Warning gibt es nur, wenn der
/// Drucker antwortet, aber etwas nicht stimmt (Zugangsdaten, LAN-Modus, unlesbare Antwort), und auch dann einmal pro
/// Fehlertext. Nach <see cref="FailuresBeforeBackoff"/> Versuchen ins Leere wird ein ausgeschalteter Drucker nur noch alle
/// <see cref="BackoffInterval"/> abgefragt, außer jemand stößt den Abgleich von Hand an.
/// </summary>
public class PrinterReachability(ILogger<PrinterReachability> logger, TimeProvider time)
{
    public const int FailuresBeforeBackoff = 3;
    public static readonly TimeSpan BackoffInterval = TimeSpan.FromMinutes(10);

    public enum Outcome
    {
        Reachable,

        /// <summary>Keine Antwort: aus, nicht im Netz, Zeitüberschreitung.</summary>
        Offline,

        /// <summary>Der Drucker antwortet, aber die Abfrage scheitert.</summary>
        Failed,
    }

    private record State(Outcome Outcome, DateTime Since, int Failures, DateTime LastAttempt, string? Error);

    private readonly ConcurrentDictionary<string, State> _states = new();

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Seit wann der Drucker nicht antwortet; <c>null</c>, wenn er erreichbar ist oder einen Fehler meldet.</summary>
    public DateTime? OfflineSince(string printerId) =>
        _states.TryGetValue(printerId, out var state) && state.Outcome == Outcome.Offline ? state.Since : null;

    /// <summary><c>true</c>, solange ein ausgeschalteter Drucker im Backoff ist und noch nicht wieder dran ist.</summary>
    public bool ShouldWait(string printerId) =>
        _states.TryGetValue(printerId, out var state) && state.Outcome == Outcome.Offline
        && state.Failures >= FailuresBeforeBackoff && Now - state.LastAttempt < BackoffInterval;

    public void Succeeded(Printer printer)
    {
        var now = Now;
        var previous = _states.GetValueOrDefault(printer.Id);
        _states[printer.Id] = new State(Outcome.Reachable, previous?.Outcome == Outcome.Reachable ? previous.Since : now, 0, now, null);
        // Nach einem Neustart weiß nur die Datenbank, dass er aus war
        var offlineSince = previous is null ? printer.OfflineSince : previous.Outcome == Outcome.Offline ? previous.Since : null;
        if (offlineSince is { } since)
            logger.LogInformation("Drucker {Printer} wieder erreichbar, war {Duration} nicht erreichbar", printer.Name, Describe(now - since));
        else if (previous?.Outcome == Outcome.Failed)
            logger.LogInformation("Drucker {Printer} antwortet wieder fehlerfrei", printer.Name);
    }

    public Outcome Failed(Printer printer, Exception ex)
    {
        var now = Now;
        var outcome = IsUnreachable(ex) ? Outcome.Offline : Outcome.Failed;
        var previous = _states.GetValueOrDefault(printer.Id);
        if (previous is null && outcome == Outcome.Offline && printer.OfflineSince is { } known)
            previous = new State(Outcome.Offline, known, FailuresBeforeBackoff, now, null);
        var same = previous?.Outcome == outcome;
        _states[printer.Id] = new State(outcome, same ? previous!.Since : now, same ? previous!.Failures + 1 : 1, now, ex.Message);

        if (outcome == Outcome.Offline)
        {
            if (!same)
                logger.LogInformation("Drucker {Printer} nicht erreichbar, vermutlich ausgeschaltet: {Reason}", printer.Name, ex.Message);
            else
                logger.LogDebug(ex, "Drucker {Printer} weiterhin nicht erreichbar", printer.Name);
        }
        else if (!same || previous!.Error != ex.Message)
            logger.LogWarning(ex, "Abfrage von Drucker {Printer} fehlgeschlagen", printer.Name);
        else
            logger.LogDebug(ex, "Abfrage von Drucker {Printer} weiterhin fehlgeschlagen", printer.Name);
        return outcome;
    }

    /// <summary>Nach dem Ändern oder Löschen des Druckers neu beginnen.</summary>
    public void Forget(string printerId) => _states.TryRemove(printerId, out _);

    /// <summary>
    /// <c>true</c>, wenn der Drucker gar nicht antwortet (Verbindung abgelehnt, keine Route, Namensauflösung, Zeitüberschreitung);
    /// <c>false</c>, wenn er antwortet, aber ablehnt oder Unlesbares schickt.
    /// </summary>
    public static bool IsUnreachable(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case HttpRequestException { StatusCode: not null }:
                case MqttConnectingFailedException:
                    return false;
                case SocketException:
                case TimeoutException:
                case TaskCanceledException:
                case MqttCommunicationTimedOutException:
                case HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError }:
                    return true;
            }
        }
        return false;
    }

    private static string Describe(TimeSpan duration) => duration switch
    {
        { TotalMinutes: < 1 } => "unter einer Minute",
        { TotalHours: < 1 } => $"{(int)duration.TotalMinutes} min",
        { TotalDays: < 2 } => $"{duration.TotalHours:0.#} h",
        _ => $"{(int)duration.TotalDays} Tage",
    };
}
