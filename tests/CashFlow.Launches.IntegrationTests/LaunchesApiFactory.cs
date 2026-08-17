using CashFlow.Launches.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Launches.IntegrationTests;

/// <summary>
/// Boots the real API - real DI graph, real middleware, real EF model - against an isolated
/// in-memory SQLite database and the in-process transport. No Docker is required to run the suite,
/// and every layer below the HTTP boundary is exercised for real.
/// </summary>
public class LaunchesApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _keepAliveConnection;

    static LaunchesApiFactory()
    {
        // These two decide which services get *registered*, so they must be visible while the
        // entry point is still composing the container - before WebApplicationFactory gets a
        // chance to layer configuration on top. Environment variables are read by
        // WebApplication.CreateBuilder itself, which makes them the right channel here.
        Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
        Environment.SetEnvironmentVariable("Messaging__Provider", "InMemory");
    }

    protected LaunchesApiFactory(bool outboxDispatcherEnabled)
    {
        OutboxDispatcherEnabled = outboxDispatcherEnabled;
        ConnectionString = $"Data Source=file:launches-{Guid.NewGuid():N}?mode=memory&cache=shared";

        // SQLite discards an in-memory database as soon as the last connection closes; this one is
        // held open for the lifetime of the fixture so the schema survives between requests.
        _keepAliveConnection = new SqliteConnection(ConnectionString);
        _keepAliveConnection.Open();
    }

    public LaunchesApiFactory() : this(outboxDispatcherEnabled: false)
    {
    }

    /// <summary>When false, outbox rows stay pending so tests can assert on them.</summary>
    private bool OutboxDispatcherEnabled { get; }

    private string ConnectionString { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:ConnectionString"] = ConnectionString,
                ["Database:ApplySchemaOnStartup"] = "true",
                ["Messaging:Provider"] = "InMemory",
                ["Outbox:Enabled"] = OutboxDispatcherEnabled ? "true" : "false",
                ["Outbox:PollIntervalMilliseconds"] = "100",
            }));
    }

    /// <summary>Runs an assertion against the service's own database.</summary>
    public async Task<T> QueryDatabaseAsync<T>(Func<LaunchesDbContext, Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LaunchesDbContext>();

        return await query(context);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _keepAliveConnection.Dispose();
            SqliteConnection.ClearAllPools();
        }
    }
}
