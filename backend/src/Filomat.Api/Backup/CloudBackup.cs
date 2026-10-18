using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Filomat.Api.Backup;

/// <summary>Wie die letzte Sicherung lief.</summary>
/// <param name="Archives">Die hochgeladenen ZIPs, z. B. <c>filomat-mon.zip</c> und <c>filomat-2026-10.zip</c>.</param>
public record BackupRun(DateTimeOffset StartedAt, DateTimeOffset FinishedAt, bool Success, List<string> Archives, long Bytes, string? Error, bool Scheduled);

/// <param name="NextRun">Die nächste nächtliche Sicherung, <c>null</c> bei nur manuellen Sicherungen.</param>
public record BackupStatus(bool Configured, bool Running, BackupRun? Last, DateTimeOffset? NextRun);

/// <summary>
/// Sichert die Datenbank jede Nacht und auf Anfrage über WebDAV in eine Nextcloud, nach dem Vorbild von YuE UI und
/// module-o-mat: eine Kopie über SQLites Online-Backup (die App kann gerade schreiben), gepackt als ZIP.
/// </summary>
/// <remarks>
/// Jede Sicherung schreibt zwei ZIPs: eins pro Wochentag (<c>filomat-mon.zip</c>, die letzten sieben Tage) und eins
/// pro Monat (<c>filomat-2026-10.zip</c>, der letzte Stand jedes Monats). In der Nextcloud wird nichts gelöscht. Wie es
/// lief, steht in <c>backup.json</c> neben der Datenbank.
/// </remarks>
public sealed class CloudBackup(
    IOptions<BackupOptions> options,
    WebDavClient webDav,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<CloudBackup> logger) : BackgroundService
{
    /// <summary>Wie oft der Zeitplan auf die Uhr schaut.</summary>
    public static TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Lock _gate = new();
    private State? _state;
    private Task? _running;
    private DateTimeOffset? _failedAt;

    private string DatabasePath => Path.GetFullPath(
        new SqliteConnectionStringBuilder(configuration.GetConnectionString("Filomat") ?? "Data Source=filomat.db").DataSource);

    private string StatePath => Path.Combine(Path.GetDirectoryName(DatabasePath)!, "backup.json");

    public BackupStatus Status
    {
        get
        {
            var settings = options.Value;
            lock (_gate)
            {
                var state = LoadState();
                return new BackupStatus(settings.Configured, _running is { IsCompleted: false }, state.Last,
                    settings.Configured ? NextRun(state) : null);
            }
        }
    }

    /// <summary>Die laufende Sicherung, damit Tests darauf warten können.</summary>
    public Task Running
    {
        get
        {
            lock (_gate) return _running ?? Task.CompletedTask;
        }
    }

    /// <summary>Startet eine Sicherung; <c>false</c>, solange eine läuft oder keine Nextcloud eingerichtet ist.</summary>
    public bool Start() => StartRun(scheduled: false);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Configured)
            logger.LogInformation("Nextcloud-Sicherung aus: {Url}, {User} und {Password} fehlen",
                BackupOptions.UrlVariable, BackupOptions.UserVariable, BackupOptions.PasswordVariable);
        using var timer = new PeriodicTimer(CheckInterval, time);
        try
        {
            do
            {
                if (Due()) StartRun(scheduled: true);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private bool Due()
    {
        var settings = options.Value;
        if (!settings.Configured || settings.DailyAt is null) return false;
        lock (_gate)
        {
            if (_running is { IsCompleted: false }) return false;
            if (_failedAt is { } failed && time.GetUtcNow() - failed < settings.RetryAfter) return false;
            return NextRun(LoadState()) is { } next && next <= time.GetUtcNow();
        }
    }

    /// <summary>Heute, solange die heutige nächtliche Sicherung noch nicht geklappt hat, sonst morgen.</summary>
    private DateTimeOffset? NextRun(State state)
    {
        var settings = options.Value;
        if (settings.DailyAt is not { } at) return null;
        var zone = settings.Zone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        var day = state.LastDaily is { } last && last >= today ? today.AddDays(1) : today;
        var local = day.ToDateTime(at);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private bool StartRun(bool scheduled)
    {
        lock (_gate)
        {
            if (!options.Value.Configured || _running is { IsCompleted: false }) return false;
            _running = Task.Run(() => RunAsync(scheduled));
            return true;
        }
    }

    private async Task RunAsync(bool scheduled)
    {
        var started = time.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(started, options.Value.Zone);
        List<string> archives = [];
        long bytes = 0;
        string? error = null;
        var scratch = Directory.CreateTempSubdirectory("filomat-backup-").FullName;
        try
        {
            var zip = Path.Combine(scratch, "filomat.zip");
            WriteArchive(zip, scratch);
            await webDav.EnsureFolderAsync(CancellationToken.None);
            foreach (var name in new[] { $"filomat-{local.DayOfWeek.ToString()[..3].ToLowerInvariant()}.zip", $"filomat-{local:yyyy-MM}.zip" })
            {
                await webDav.PutAsync(name, zip, CancellationToken.None);
                archives.Add(name);
                bytes += new FileInfo(zip).Length;
            }
        }
        catch (Exception ex) when (ex is WebDavException or HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or SqliteException)
        {
            error = ex is TaskCanceledException ? "Die Nextcloud antwortet nicht" : ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Nextcloud-Sicherung unerwartet fehlgeschlagen");
            error = ex.Message;
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        lock (_gate)
        {
            var state = LoadState();
            state.Last = new BackupRun(started, time.GetUtcNow(), error is null, archives, bytes, error, scheduled);
            if (scheduled && error is null) state.LastDaily = DateOnly.FromDateTime(local.DateTime);
            _failedAt = error is null ? null : time.GetUtcNow();
            SaveState(state);
        }
        if (error is null)
            logger.LogInformation("Nextcloud-Sicherung fertig: {Archives}, {Bytes} Bytes", string.Join(", ", archives), bytes);
        else
            logger.LogWarning("Nextcloud-Sicherung fehlgeschlagen: {Error}", error);
    }

    /// <summary>Kopiert die Datenbank über SQLites Online-Backup, das auch während Schreibzugriffen einen stimmigen Stand liefert.</summary>
    private void WriteArchive(string zip, string scratch)
    {
        var copy = Path.Combine(scratch, "filomat.db");
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Pooling = false }.ToString()))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(copy, "filomat.db", CompressionLevel.Optimal);
    }

    private State LoadState()
    {
        if (_state is not null) return _state;
        try
        {
            _state = File.Exists(StatePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "{Path} nicht lesbar", StatePath);
        }
        return _state ??= new State();
    }

    private void SaveState(State state)
    {
        _state = state;
        try
        {
            var temp = StatePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
            File.Move(temp, StatePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "{Path} nicht schreibbar", StatePath);
        }
    }

    private sealed class State
    {
        public BackupRun? Last { get; set; }

        /// <summary>Der lokale Tag der letzten erfolgreichen nächtlichen Sicherung.</summary>
        public DateOnly? LastDaily { get; set; }
    }
}
