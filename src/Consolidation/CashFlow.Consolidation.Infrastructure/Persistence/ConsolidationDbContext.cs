using CashFlow.Consolidation.Domain.DailyBalances;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

/// <summary>
/// Database of the Consolidation service. Holds only the projection and its deduplication ledger -
/// it never stores entries, which is exactly why it can be rebuilt from the event stream.
/// </summary>
public sealed class ConsolidationDbContext(DbContextOptions<ConsolidationDbContext> options) : DbContext(options)
{
    public const string Schema = "consolidation";

    public DbSet<DailyBalance> DailyBalances => Set<DailyBalance>();

    internal DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConsolidationDbContext).Assembly);

        SqliteCompatibility.ApplyDateTimeOffsetConverters(modelBuilder, Database);
    }
}
