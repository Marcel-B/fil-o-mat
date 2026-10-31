namespace Filomat.Api.Monitoring;

/// <summary>
/// Reicht jede Logzeile an den <see cref="LokiShipper"/> weiter; die Filter aus <c>Logging:LogLevel</c> greifen vorher wie
/// gewohnt. Kategorie und Exception stehen in der Zeile, die Quelle kommt aus dem Namensraum.
/// </summary>
public sealed class LokiLoggerProvider(LokiShipper shipper) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LokiLogger(shipper, categoryName, SourceOf(categoryName));

    public void Dispose()
    {
    }

    internal static string MapLevel(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warning",
        _ => "error",
    };

    /// <summary>Wenige feste Werte für das Label <c>source</c>; alles Übrige, auch ASP.NET selbst, ist <c>server</c>.</summary>
    internal static string SourceOf(string category) => category switch
    {
        _ when category.StartsWith("Filomat.Api.Printers", StringComparison.Ordinal) => "printers",
        _ when category.StartsWith("Filomat.Api.Backup", StringComparison.Ordinal) => "backup",
        _ when category.StartsWith("Filomat.Api.Notifications", StringComparison.Ordinal) => "notifications",
        _ => "server",
    };

    internal static string FormatLine(string category, string message, Exception? exception) =>
        exception is null ? $"[{category}] {message}" : $"[{category}] {message}{Environment.NewLine}{exception}";

    private sealed class LokiLogger(LokiShipper shipper, string category, string source) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            shipper.Enqueue(DateTimeOffset.UtcNow, MapLevel(logLevel), source, FormatLine(category, formatter(state, exception), exception));
        }
    }
}
