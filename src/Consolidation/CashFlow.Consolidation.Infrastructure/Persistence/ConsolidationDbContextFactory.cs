using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

/// <summary>Design-time factory used by the EF Core tooling. See the Launches counterpart.</summary>
internal sealed class ConsolidationDbContextFactory : IDesignTimeDbContextFactory<ConsolidationDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=cashflow_consolidation;Username=cashflow;Password=cashflow";

    public ConsolidationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CASHFLOW_CONSOLIDATION_CONNECTION")
            ?? DesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ConsolidationDbContext.Schema))
            .Options;

        return new ConsolidationDbContext(options);
    }
}
