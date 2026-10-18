using Filomat.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Data;

public static class DatabaseInitializer
{
    /// <summary>Wendet Migrationen an und legt fehlende Standard-Container an.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FilomatDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var existing = await db.Containers
            .Where(c => c.Id == Container.PrinterId || c.Id == Container.LooseId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        var missing = Container.StandardContainers(DateTime.UtcNow).Where(c => !existing.Contains(c.Id));
        db.Containers.AddRange(missing);
        await db.SaveChangesAsync(cancellationToken);
    }
}
