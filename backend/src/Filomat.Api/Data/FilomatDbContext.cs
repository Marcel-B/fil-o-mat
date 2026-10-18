using Filomat.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Filomat.Api.Data;

public class FilomatDbContext(DbContextOptions<FilomatDbContext> options) : DbContext(options)
{
    public DbSet<Container> Containers => Set<Container>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<Consumption> Consumptions => Set<Consumption>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite speichert keine DateTimeKind; alle Zeitpunkte sind UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Container>(container =>
        {
            container.HasKey(c => c.Id);
            container.HasIndex(c => c.RfidTag).IsUnique();
        });

        modelBuilder.Entity<Item>(item =>
        {
            item.HasKey(i => i.Id);
            item.HasIndex(i => i.RfidTag);
            item.HasOne<Container>()
                .WithMany()
                .HasForeignKey(i => i.ContainerId)
                .OnDelete(DeleteBehavior.SetNull);
            // Tag-Daten als JSON-Spalte: das OpenSpool-Format kann so ohne Migration wachsen.
            item.OwnsOne(i => i.ItemData, data => data.ToJson());
        });

        modelBuilder.Entity<Printer>(printer =>
        {
            printer.HasKey(p => p.Id);
            printer.Property(p => p.Type).HasConversion<string>();
            printer.HasOne<Container>()
                .WithMany()
                .HasForeignKey(p => p.ContainerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Consumption>(consumption =>
        {
            consumption.HasKey(c => c.Id);
            consumption.HasIndex(c => new { c.PrinterId, c.JobId, c.Tool }).IsUnique();
            consumption.HasIndex(c => c.ItemId);
            consumption.HasOne<Printer>()
                .WithMany()
                .HasForeignKey(c => c.PrinterId)
                .OnDelete(DeleteBehavior.Cascade);
            consumption.HasOne<Item>()
                .WithMany()
                .HasForeignKey(c => c.ItemId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

internal class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.ToUniversalTime(),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
