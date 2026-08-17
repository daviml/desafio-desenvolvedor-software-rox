using CashFlow.Launches.Domain.Entries;
using CashFlow.Launches.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Launches.Infrastructure.Persistence;

/// <summary>
/// Write-side database of the Launches service. The outbox table lives here on purpose:
/// it is what lets a business change and its integration event share one transaction.
/// </summary>
public sealed class LaunchesDbContext(DbContextOptions<LaunchesDbContext> options) : DbContext(options)
{
    public const string Schema = "launches";

    public DbSet<Entry> Entries => Set<Entry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LaunchesDbContext).Assembly);

        SqliteCompatibility.ApplyDateTimeOffsetConverters(modelBuilder, Database);
    }
}
