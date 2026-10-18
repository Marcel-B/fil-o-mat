using Filomat.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Printers;

/// <summary>Gleicht alle aktiven Drucker regelmäßig ab (Intervall: <c>Filomat:PrinterSyncInterval</c>).</summary>
public class PrinterSyncService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<PrinterSyncService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = configuration.GetValue("Filomat:PrinterSyncInterval", TimeSpan.FromMinutes(2));
        if (interval <= TimeSpan.Zero) return;

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<FilomatDbContext>();
                var sync = scope.ServiceProvider.GetRequiredService<ConsumptionSync>();
                foreach (var printer in await db.Printers.Where(p => p.Enabled).ToListAsync(stoppingToken))
                    await sync.SyncAsync(printer, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Drucker-Abgleich fehlgeschlagen");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
