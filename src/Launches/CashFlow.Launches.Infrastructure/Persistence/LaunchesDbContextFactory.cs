using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CashFlow.Launches.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core tooling ("dotnet ef migrations add"). Migrations are authored against
/// PostgreSQL, which is the provider both the container image and production run on.
/// </summary>
internal sealed class LaunchesDbContextFactory : IDesignTimeDbContextFactory<LaunchesDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=cashflow_launches;Username=cashflow;Password=cashflow";

    public LaunchesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CASHFLOW_LAUNCHES_CONNECTION")
            ?? DesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<LaunchesDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", LaunchesDbContext.Schema))
            .Options;

        return new LaunchesDbContext(options);
    }
}
