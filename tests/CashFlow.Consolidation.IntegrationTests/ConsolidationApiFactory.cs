using CashFlow.Consolidation.Infrastructure.Persistence;
using CashFlow.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Consolidation.IntegrationTests;

/// <summary>
/// Boots the real Consolidation API against an isolated in-memory SQLite database. Integration
/// events are fed through the same dispatcher the RabbitMQ consumer uses, so the projection path
/// under test is the production one minus the broker hop.
/// </summary>
public sealed class ConsolidationApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _keepAliveConnection;

    static ConsolidationApiFactory()
    {
        // Registration-time switches: they must be visible while the entry point is still
        // composing the container, which is what environment variables guarantee.
        Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
        Environment.SetEnvironmentVariable("Messaging__Provider", "InMemory");
    }

    public ConsolidationApiFactory()
    {
        ConnectionString = $"Data Source=file:consolidation-{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAliveConnection = new SqliteConnection(ConnectionString);
        _keepAliveConnection.Open();
    }

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
            }));
    }

    /// <summary>Delivers an integration event exactly as the broker consumer would.</summary>
    public async Task DispatchAsync(IntegrationEvent integrationEvent)
    {
        await using var scope = Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        await dispatcher.DispatchAsync(integrationEvent, CancellationToken.None);
    }

    public async Task<T> QueryDatabaseAsync<T>(Func<ConsolidationDbContext, Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();

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
