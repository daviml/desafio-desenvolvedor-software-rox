using System.ComponentModel.DataAnnotations;

namespace CashFlow.Launches.Infrastructure.Persistence;

/// <summary>Supported storage engines.</summary>
public enum DatabaseProvider
{
    /// <summary>Default for local runs and production.</summary>
    Postgres = 0,

    /// <summary>File or in-memory SQLite. Used by the automated tests and the "no Docker" profile.</summary>
    Sqlite = 1,
}

/// <summary>Database settings, bound from the "Database" configuration section.</summary>
/// <remarks>
/// Deliberately duplicated per service rather than shared: the two bounded contexts own their own
/// schema and must remain independently deployable and independently configurable.
/// </remarks>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Postgres;

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Applies the schema at start-up. Convenient here; a release pipeline would own it in production.</summary>
    public bool ApplySchemaOnStartup { get; set; } = true;

    /// <summary>Transient-failure retries performed by the provider itself (connection blips, failover).</summary>
    [Range(0, 20)]
    public int MaxRetryCount { get; set; } = 5;

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool EnableSensitiveDataLogging { get; set; }
}
